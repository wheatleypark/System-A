using Microsoft.Azure.WebJobs;
using System.Net.Mail;
using System.Net;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using System.Threading;
using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace WpsBehaviour.Mailer
{
  public static class MailerFunction
  {
    static SemaphoreSlim _throttler = new SemaphoreSlim(1, 1);
    private const int BATCH_SIZE = 20;
    private const int DELAY = 2500;

    [FunctionName("sendmail")]
    public static async Task Run([QueueTrigger("emailtriggers")]string trigger, [Queue("emailtriggers")]IAsyncCollector<string> triggerQueue, [Queue("emails")]CloudQueue emailQueue, [Blob("emails")]CloudBlobContainer emailContainer, TraceWriter log, Microsoft.Azure.WebJobs.ExecutionContext context)
    {
      if (_throttler.CurrentCount == 0) {
        log.Info($"Chained email triggers in progress.");
        return;
      }

      try {
        await _throttler.WaitAsync();

        var config = Config.Instance(Path.Combine(context.FunctionDirectory, Config.CONFIG_PATH));
        var cred = new NetworkCredential(config.SenderEmail, config.SenderPassword);

        int count;

        using (var client = new SmtpClient("smtp.gmail.com") { Port = 587, Credentials = cred, EnableSsl = true }) {
          
          for (count = 0; count < BATCH_SIZE; count++) {

            var item = await emailQueue.GetMessageAsync(TimeSpan.FromMilliseconds(DELAY), null, null);
            if (item == null) break;

            var blob = emailContainer.GetBlockBlobReference(item.AsString);
            var email = JsonConvert.DeserializeObject<Email>(await blob.DownloadTextAsync());

            var message = new MailMessage {
              From = new MailAddress(config.SenderEmail, email.FromName),
              Subject = email.Subject,
              IsBodyHtml = true,
              Body = email.Body
            };

            message.To.Add(email.To);

            if (email.AttachmentHtml != null) {
              message.Attachments.Add(Attachment.CreateAttachmentFromString(email.AttachmentHtml, "Attachment.html", Encoding.ASCII, "text/html"));
            }

            try {
              await client.SendMailAsync(message);
              await emailQueue.DeleteMessageAsync(item);
              await blob.DeleteAsync();
              log.Info($"Emailed {email.To}");
            } catch (Exception exc) {
              log.Error($"Failed to send message from {email.FromName} to {email.To}: {exc.Message}");
              if (item.DequeueCount >= 5) {
                await emailQueue.DeleteMessageAsync(item);
                if (blob != null) {
                  await blob.DeleteIfExistsAsync();
                }
              }
            }
            await Task.Delay(DELAY);
          }
        }
        log.Info($"Processed {count} emails.");
        if (count < 20) return;

        await Task.Delay(60000);

      } finally {
        _throttler.Release();
      }

      await triggerQueue.AddAsync(string.Empty);
    }
  }
}