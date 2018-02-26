using Microsoft.Azure.WebJobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace WpsBehaviour.Absence
{
  public class Mailer
  {
    private string SenderEmail { get; set; }
    private SmtpClient Client { get; set; }
    private EmailTemplate StudentTemplate { get; set; }
    private EmailTemplate ParentTemplate { get; set; }
    private IDictionary<string, string> Contacts { get; set; }
    public string RecipientOverride { get; set; }
    IAsyncCollector<string> Queue { get; set; }
    CloudBlobContainer Container { get; set; }

    public Mailer(string senderEmail, IAsyncCollector<string> queue, CloudBlobContainer container, EmailTemplate studentTemplate, EmailTemplate parentTemplate, IDictionary<string, string> contacts, string recipientOverride)
    {
      SenderEmail = senderEmail;
      StudentTemplate = studentTemplate;
      ParentTemplate = parentTemplate;
      Contacts = contacts;
      RecipientOverride = recipientOverride;
      Queue = queue;
      Container = container;
    }


    public async Task QueueEmailsAsync(IList<AbsenceIncident> absences)
    {
      if (absences.Count == 0) return;

      var students = absences.GroupBy(o => o.StudentEmail).Select(o => new { First = o.First(), Items = o }).Select(o => new StudentAbsence {
        Forename = o.First.Forename,
        Surname = o.First.Surname,
        StudentEmail = o.First.StudentEmail,
        Reg = o.First.Reg,
        ParentEmail = o.First.ParentEmail,
        ParentSalutation = o.First.ParentSalutation,
        MissedSessions = o.Items.Select(s => new StudentAbsence.SessionAbsence {
          Class = s.Class,
          Teacher = s.Teacher,
          Session = s.Missed.TrimStart('_')
        }).ToList()
      });

      foreach (var student in students) {
        // Email students
        var subject = ReplaceTemplateFields(StudentTemplate.Subject, student);
        var body = ReplaceTemplateFields(StudentTemplate.Body, student);
        await QueueEmailAsync(student.StudentEmail, subject, body);

        // Email parents
        if (!string.IsNullOrEmpty(student.ParentEmail)) {
          subject = ReplaceTemplateFields(ParentTemplate.Subject, student);
          body = ReplaceTemplateFields(ParentTemplate.Body, student);
          await QueueEmailAsync(student.ParentEmail, subject, body);
        }
      }

      // Email tutors and HOY
      const string tokenAll = "Sixth Form";
      var msgAllLessons = string.Empty;
      var msgAllReg = string.Empty;
      foreach (var tutorGroup in students.GroupBy(o => o.Reg)) {
        var missedLessons = tutorGroup.Where(s => s.MissedSessions.Any(m => m.Class != null)).ToList();
        var missedReg = tutorGroup.Where(s => s.MissedSessions.Any(m => m.Class == null)).Select(s => $"{s.Surname} {s.Forename}").ToList();
        var message = string.Empty;
        if (missedLessons.Count > 0) {
          var msgLessons = "<ul>" + string.Concat(missedLessons.Select(o => $"<li>{o.Surname}, {o.Forename}{o.MissedLessonBullets}</li>")) + "</ul>";
          message += "<b style=\"color: Red\">Missed lessons</b>" + msgLessons;
          msgAllLessons += $"<b>{tutorGroup.Key}</b>" + msgLessons;
        }
        if (missedReg.Count > 0) {
          var msgReg = "<ul>" + string.Concat(missedReg.Select(o => $"<li>{o}</li>")) + "</ul>";
          message += "<b style=\"color: Red\">Missed registration</b>" + msgReg;
          msgAllReg += $"<b>{tutorGroup.Key}</b>" + msgReg;
        }
        await QueueEmailAsync(Contacts[tutorGroup.Key], $"{tutorGroup.Key} Absences - {DateTime.Today.ToString("dddd d MMMM yyyy")}", message);
      }
      var msgAll = string.Empty;
      if (msgAllLessons.Length > 0) {
        msgAll += "<h3 style=\"color: Red\">Missed lessons</h3>" + msgAllLessons;
      }
      if (msgAllReg.Length > 0) {
        msgAll += "<h3 style=\"color: Red\">Missed registration</h3>" + msgAllReg;
      }
      await QueueEmailAsync(Contacts[tokenAll], $"{tokenAll} Absences - {DateTime.Today.ToString("dddd d MMMM yyyy")}", msgAll);
    }


    private async Task QueueEmailAsync(string recipient, string subject, string body)
    {
      if (RecipientOverride != null) {
        recipient = RecipientOverride;
      }

      var email = new Email {
        To = recipient,
        Body = body,
        Subject = subject,
        FromName = "WPS Attendance"
      };

      var serialized = JsonConvert.SerializeObject(email);
      var guid = Guid.NewGuid().ToString("D");

      var blob = Container.GetBlockBlobReference(guid);
      await blob.UploadTextAsync(serialized);
      await Queue.AddAsync(guid);
    }


    private static string ReplaceTemplateFields(string template, StudentAbsence absence)
    {
      var rtn = template;
      foreach (var property in typeof(StudentAbsence).GetProperties()) {
        rtn = rtn.Replace($"{{{{{property.Name}}}}}", property.GetValue(absence)?.ToString() ?? string.Empty);
      }
      return rtn.Replace("\n", "<br />");
    }


  }
}