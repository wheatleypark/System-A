using Microsoft.Azure.WebJobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace WpsBehaviour.Behaviour
{
  public class Mailer
  {
    private string SenderEmail { get; set; }
    private IList<EmailTemplate> Templates { get; set; }
    private IDictionary<string, string> Contacts { get; set; }
    public string SpreadsheetId { get; set; }
    public string RecipientOverride { get; set; }
    IAsyncCollector<string> Queue { get; set; }
    CloudBlobContainer Container { get; set; }

    private Dictionary<string, int> ParentTemplatePicker = new Dictionary<string, int>()
    {
      { "Chromebook", 2 },
      { "Equipment not present", 3 },
      { "Homework not complete", 4 },
      { "Late to lesson", 5 },
      { "Misuse of Chromebook", 6 },
      { "Mobile phone use", 7 },
      { "Out of class", 8 },
      { "Swearing", 9 }
    };

    public Mailer(string senderEmail, IAsyncCollector<string> queue, CloudBlobContainer container, IList<EmailTemplate> templates, IDictionary<string, string> contacts, string spreadsheetId, string recipientOverride)
    {
      SenderEmail = senderEmail;
      Templates = templates;
      Contacts = contacts;
      SpreadsheetId = spreadsheetId;
      RecipientOverride = recipientOverride;
      Queue = queue;
      Container = container;
    }


    public async Task QueueEmailsAsync(IList<BehaviourIncident> incidents)
    {
      var nextWorkingDay = Enumerable.Range(1, 3).Select(o => DateTime.Today.AddDays(o)).First(o => o.DayOfWeek != DayOfWeek.Saturday && o.DayOfWeek != DayOfWeek.Sunday);

      const string pad = "style=\"padding: 5px; border: 1px solid #999\"";

      var nextDayIncidents = incidents.Where(o => o.Status == Status.Pending && o.DetentionDate == nextWorkingDay).OrderBy(o => o.Reg).ThenBy(o => o.Surname).ThenBy(o => o.Forename).ToList();
      if (nextDayIncidents.Count > 0)
      {
        var dateString = nextWorkingDay.ToString("dddd d MMMM yyyy");
        const string registers = "Registers";
        var tableHeader = $"<table style=\"border-collapse: collapse\"><thead style=\"font-style: italic\"><tr><td {pad}>Student</td><td {pad}>Reg</td><td {pad}>Reason</td><td {pad}>Period</td><td {pad}>Subject</td><td {pad}>Staff</td></tr></thead><tbody>";
        string createRow(BehaviourIncident student) => $"<tr><td {pad}><b>{student.Surname}, {student.Forename}</b></td><td {pad}><b>{student.Reg}</b></td><td {pad}>{student.Incident}</td><td {pad}>{student.Period}</td><td {pad}>{student.Subject}</td><td {pad}>{student.Staff}</td></tr>";

        // Email curriculum services
        var sbRegisters = new StringBuilder("<html><head><title>Lunchtime Detention List</title></head><body>");
        foreach (var tutorGroup in nextDayIncidents.GroupBy(o => o.Reg))
        {
          sbRegisters.Append($"<h2 style=\"page-break-before: always\">{tutorGroup.Key} Lunchtime Detentions - {dateString}</h2><p>Please remind the following students to attend their lunchtime detentions today.</p>" + tableHeader);
          foreach (var student in tutorGroup)
          {
            sbRegisters.Append(createRow(student));
          }
          sbRegisters.Append("</tbody></table>");
        }
        sbRegisters.Append("</body></html>");
        await QueueEmailAsync(Contacts[registers], $"Lunchtime Detention Lists - {dateString}", "Please print the attached student lists and put them into register folders.", sbRegisters.ToString());

        // Email HOYs and AHOYs
        foreach (var yearGroup in nextDayIncidents.GroupBy(o => o.Year))
        {
          var sbYear = new StringBuilder(tableHeader);
          foreach (var student in yearGroup)
          {
            sbYear.Append(createRow(student));
          }
          sbYear.Append("</tbody></table><br /><br /><a href=\"https://docs.google.com/spreadsheets/d/" + SpreadsheetId + "/edit\">Behaviour sheet</a>");
          await QueueEmailAsync(Contacts[yearGroup.Key], $"Y{yearGroup.Key} Lunchtime Detention List - {dateString}", sbYear.ToString());
        }
      }

      // Email students and parents
      foreach (var incident in incidents.Where(o => o.EmailRequired))
      {
        var template = Templates[incident.PreviousDetentionDate == null ? 0 : 1];
        var subject = ReplaceTemplateFields(template.Subject, incident);
        var body = ReplaceTemplateFields(template.Body, incident);
        await QueueEmailAsync(incident.StudentEmail, subject, body);

        if (!string.IsNullOrEmpty(incident.ParentEmail) && incident.PreviousDetentionDate == null)
        {
          if (!ParentTemplatePicker.TryGetValue(incident.Incident, out int index)) continue;
          template = Templates[index];
          subject = ReplaceTemplateFields(template.Subject, incident);
          body = ReplaceTemplateFields(template.Body, incident);
          await QueueEmailAsync(incident.ParentEmail, subject, body);
        }
      }

      // Email about overdue detentions
      foreach (var yearGroup in incidents.Where(o => o.Status == Status.Overdue).GroupBy(o => o.Year))
      {
        var sbOverdue = new StringBuilder("<span style=\"color: Red; font-weight: bold\">These past detentions need to be Resolved, Rescheduled, Cancelled or Escalated. Please update their status on the spreadsheet.</span><br /><br />");
        sbOverdue.Append($"<table style=\"border-collapse: collapse\"><thead style=\"font-style: italic\"><tr><td {pad}>Student</td><td {pad}>Detention Date</td></tr></thead><tbody>");
        foreach (var student in yearGroup)
        {
          sbOverdue.Append($"<tr><td {pad}><b>{student.Surname}, {student.Forename}</b></td><td {pad}><b>{student.DetentionDate?.ToString("ddd d MMM") ?? string.Empty}</b></td></tr>");
        }
        sbOverdue.Append("</tbody></table><br /><br /><a href=\"https://docs.google.com/spreadsheets/d/" + SpreadsheetId + "/edit\">Behaviour sheet</a>");
        await QueueEmailAsync(Contacts[yearGroup.Key], $"Y{yearGroup.Key} Overdue Detentions as of {DateTime.Today.ToString("d/M/yy")} - Action Needed", sbOverdue.ToString());
      }
    }


    private async Task QueueEmailAsync(string recipient, string subject, string body, string attachmentHtml = null)
    {
      if (RecipientOverride != null) {
        recipient = RecipientOverride;
      }

      var email = new Email {
        To = recipient,
        Body = body,
        Subject = subject,
        FromName = "WPS Behaviour",
        AttachmentHtml = attachmentHtml
      };

      var serialized = JsonConvert.SerializeObject(email);
      var guid = Guid.NewGuid().ToString("D");

      var blob = Container.GetBlockBlobReference(guid);

      for (var attempt = 1; attempt <= 5; attempt++)
      {
        try
        {
          await blob.UploadTextAsync(serialized);
          await Queue.AddAsync(guid);
          return;
        }
        catch when (attempt < 5)
        {
          await Task.Delay(2000 * attempt);
        }
      }
      
    }


    private static string ReplaceTemplateFields(string template, BehaviourIncident incident)
    {
      var rtn = template;
      foreach (var property in typeof(BehaviourIncident).GetProperties()) {
        rtn = rtn.Replace($"{{{{{property.Name}}}}}", PropertyMapper.DisplayValue(property.GetValue(incident), property.Name, true)?.ToString() ?? string.Empty);
      }
      return rtn.Replace("\n", "<br />");
    }
  }
}