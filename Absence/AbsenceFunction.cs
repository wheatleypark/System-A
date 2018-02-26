using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

namespace WpsBehaviour.Absence
{
  public static class AbsenceFunction
  {
    [FunctionName("processabsence")]
    public static async Task<HttpResponseMessage> Run([HttpTrigger("POST")]HttpRequestMessage req, [Queue("emails")]IAsyncCollector<string> emailQueue, [Blob("emails")]CloudBlobContainer emailContainer, [Queue("emailtriggers")]IAsyncCollector<string> triggerQueue, TraceWriter log, ExecutionContext context)
    {
      var config = Config.Instance(context.FunctionDirectory);
      var spreadsheet = new AbsenceSpreadsheet(config.AbsenceSpreadsheetId, config.Key);
      await spreadsheet.GetAppDataAsync();

#if !DEBUG
      if (spreadsheet.AppData.LastExported >= DateTime.Today) {
        log.Error("Export has already run today.");
        return req.CreateResponse(HttpStatusCode.Conflict, new { error = "Export has already run today." });
      }
#endif
      await Task.Delay(5000);

      var newAbsences = (await req.Content.ReadAsAsync<IList<AbsenceIncident>>()).OrderBy(o => o.Student).ToList();
      log.Info($"Request contains {newAbsences.Count} absences.");

      await spreadsheet.AddAbsencesAsync(newAbsences);
      await spreadsheet.WriteLastEditDateAsync();

      var mailer = new Mailer(config.SenderEmail, emailQueue, emailContainer, spreadsheet.AppData.EmailTemplates[0], spreadsheet.AppData.EmailTemplates[1], spreadsheet.AppData.Contacts, config.DebugRecipientEmail);
      await mailer.QueueEmailsAsync(newAbsences);
      await triggerQueue.AddAsync(string.Empty);

      log.Info("Function completed successfully.");
      return req.CreateResponse(HttpStatusCode.OK);
    }


  }
}