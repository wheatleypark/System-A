using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

namespace WpsBehaviour.Behaviour
{
  public static class BehaviourFunction
  {
    private const int MAX_ROWS = 200000000;
    private const string EXPECTATION_PREFIX = "Expectation - ";
    private const string EQUIPMENT_INCIDENT = EXPECTATION_PREFIX + "Equipment not present";


    [FunctionName("processbehaviour")]
    public static async Task<HttpResponseMessage> Run([HttpTrigger("POST")]HttpRequestMessage req, [Blob("emails")]CloudBlobContainer emailContainer, [Queue("emails")]IAsyncCollector<string> emailQueue, [Queue("emailtriggers")]IAsyncCollector<string> triggerQueue, TraceWriter log, ExecutionContext context)
    {
      var config = Config.Instance(context.FunctionDirectory);
      var spreadsheet = new BehaviourSpreadsheet(config.BehaviourSpreadsheetId, config.Key);
      await spreadsheet.GetTrackerDataAsync();

#if !DEBUG
      if (spreadsheet.Tracker.LastExported >= DateTime.Today) {
        log.Error("Export has already run today.");
        return req.CreateResponse(HttpStatusCode.Conflict, new { error = "Export has already run today." });
      }
#endif

      // Append new expectations incidents
      var newIncidents = await ReadIncidentsAsync(req.Content);
      log.Info($"Request contains {newIncidents[true].Count()} expectation and {newIncidents[false].Count()} behaviour incidents.");
      await spreadsheet.AddIncidentsAsync(SheetNames.Expectations, newIncidents[true].ToList());

      // Fetch and parse spreadsheet data
      var sheets = await spreadsheet.GetSheetsAsync($"{SheetNames.Expectations}!{spreadsheet.Tracker.StartRow}:{MAX_ROWS}", SheetNames.Contacts, SheetNames.DetentionDays, SheetNames.EmailTemplates);
      var (unresolved, newStartRow) = Readers.ReadUnresolved(sheets[0], spreadsheet.Tracker.ExpectationColumns, spreadsheet.Tracker.StartRow);
      var contacts = Readers.ReadContacts(sheets[1]);
      var detentionDays = Readers.ReadDetentionDays(sheets[2]);
      var emailTemplates = Readers.ReadTemplates(sheets[3]);

      // Append new behaviour incidents
      var escalated = Escalate(unresolved.Where(o => o.Status == Status.Escalate).ToList());
      await spreadsheet.AddIncidentsAsync(SheetNames.Incidents, newIncidents[false].Union(escalated).ToList());

      // Set detentions
      CalculateDetentions(unresolved, detentionDays);
      await spreadsheet.UpdateDetentionsAsync(unresolved.Where(o => o.IsChanged).ToList());

      // Delete cancelled detentions
      await spreadsheet.DeleteIncidentsAsync(unresolved.Where(o => o.Status == Status.Cancel).ToList());

      // Email students, parents and contacts
      var mailer = new Mailer(config.SenderEmail, emailQueue, emailContainer, emailTemplates, contacts, config.BehaviourSpreadsheetId, config.DebugRecipientEmail);
      await mailer.QueueEmailsAsync(unresolved.ToList());
      await triggerQueue.AddAsync(string.Empty);

      // Sort expectations sheet
      await spreadsheet.SortExpectationsSheet(spreadsheet.Tracker.ExpectationColumns);

      // Write last edit date and new start row
      await spreadsheet.WriteTrackerDataAsync(newStartRow);
      
      // Update calendar events
      var calendarChanges = unresolved.Where(o => o.EmailRequired || o.Status == Status.Cancel || o.PreviousDetentionDate != null).ToList();
      if (config.DebugRecipientEmail == null) {
        await StudentCalendar.UpdateDetentionsAsync(calendarChanges, config.Key, config.DetentionStartTimeSpan, config.DetentionEndTimeSpan, config.DebugRecipientEmail);
      }

      log.Info("Function completed successfully.");
      return req.CreateResponse(HttpStatusCode.OK);
    }


    public static async Task<ILookup<bool, BehaviourIncident>> ReadIncidentsAsync(HttpContent content)
    {
      var incidents = await content.ReadAsAsync<IList<BehaviourIncident>>();
      
      // Delete all but the first equipment sanction
      foreach (var student in incidents.Where(o => o.Incident == EQUIPMENT_INCIDENT).GroupBy(o => o.StudentEmail)) {
        foreach (var incident in student.OrderBy(o => o.Period).Skip(1)) {
          incidents.Remove(incident);
        }
      }

      // Remove 'Expectation' prefix
      var lookup = incidents.ToLookup(o => o.Incident.StartsWith(EXPECTATION_PREFIX));
      foreach (var incident in lookup[true]) {
        incident.Incident = incident.Incident.Substring(EXPECTATION_PREFIX.Length);
      }
      return lookup;
    }


    public static IList<BehaviourIncident> Escalate(IList<BehaviourIncident> incidents)
    {
      foreach (var incident in incidents) {
        incident.Status = (incident.DetentionDate == null || incident.DetentionDate.Value > DateTime.Today) ? Status.Pending : Status.Resolved;
        incident.IsChanged = true;
      }

      return incidents.Where(o => o.Status == Status.Resolved).Select(o => new BehaviourIncident {
        Date = DateTime.Today,
        Surname = o.Surname,
        Forename = o.Forename,
        Incident = "Failure to attend detention",
        Staff = string.Empty,
        Period = string.Empty,
        Class = string.Empty,
        Subject = string.Empty,
        Time = string.Empty,
        Location = string.Empty,
        Comments = $"Did not attend detention on {o.DetentionDate.Value.ToString("ddd d MMM")}, for {o.Incident} [{o.Date.ToString("ddd d MMM")}, {o.Period}, {o.Class},{o.Staff}]",
        Reg = o.Reg,
        Gender = o.Gender,
        PP = o.PP,
        SEN = o.SEN,
        StudentEmail = o.StudentEmail,
        ParentSalutation = o.ParentSalutation,
        ParentEmail = o.ParentEmail
      }).ToList();
    }


    private static void CalculateDetentions(IList<BehaviourIncident> incidents, IList<DateTime> detentionDays)
    {
      foreach (var incident in incidents.Where(o => o.Status == Status.Pending && o.DetentionDate <= DateTime.Today)) {
        incident.Status = Status.Overdue;
        incident.IsChanged = true;
      }

      foreach (var incident in incidents.Where(o => o.Status == Status.Reschedule)) {
        if (incident.DetentionDate <= DateTime.Today) {
          incident.PreviousDetentionDate = incident.DetentionDate;
          incident.DetentionDate = null;
        }
        incident.IsChanged = true;
        incident.Status = Status.Pending;
      }
      
      var incidentsByStudent = incidents.Where(o => o.Status == Status.Pending).GroupBy(o => o.StudentEmail);

      foreach (var myIncidents in incidentsByStudent) {
        var used = new HashSet<DateTime>(myIncidents.Where(o => o.DetentionDate != null).Select(o => o.DetentionDate.Value));
        foreach (var incident in myIncidents.Where(o => o.DetentionDate == null)) {
          var date = detentionDays.FirstOrDefault(o => o > DateTime.Today && !used.Contains(o));
          if (date != default) {
            incident.DetentionDate = date;
            incident.IsChanged = true;
            incident.EmailRequired = true;
            used.Add(date);
          }
        }
      }
    }


  }
}