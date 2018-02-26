using Google.Apis.Calendar.v3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using System.Threading.Tasks;
using System;
using Google.Apis.Calendar.v3.Data;
using System.Linq;
using System.Collections.Generic;

namespace WpsBehaviour.Behaviour
{
  public class StudentCalendar : IDisposable
  {
    private CalendarService Service { get; set; }
    private TimeSpan DetentionStart { set; get; }
    private TimeSpan DetentionEnd { set; get; }
    private const string PRIMARY_CALENDAR = "primary";
    private const string DETENTION_TITLE = "Lunchtime detention";
    private const string NOTIFY_POPUP = "popup";
    private const string COLOUR_RED = "4";
    private static TimeZoneInfo tzi = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");

    public StudentCalendar(string emailAddress, string key, TimeSpan detentionStart, TimeSpan detentionEnd, string recipientOverride)
    {
      if (recipientOverride != null)
      {
        emailAddress = recipientOverride;
      }

      var credential = GoogleCredential.FromJson(key).CreateScoped(CalendarService.Scope.Calendar).CreateWithUser(emailAddress);

      Service = new CalendarService(new BaseClientService.Initializer()
      {
        HttpClientInitializer = credential,
        ApplicationName = Config.APP_NAME
      });

      DetentionStart = detentionStart;
      DetentionEnd = detentionEnd;
    }


    private async Task AddDetentionAsync(BehaviourIncident incident)
    {
      var ev = new Event()
      {
        Summary = DETENTION_TITLE,
        Start = new EventDateTime() { DateTimeRaw = ConvertToDateTimeRaw(incident.DetentionDate.Value.Add(DetentionStart)) },
        End = new EventDateTime() { DateTimeRaw = ConvertToDateTimeRaw(incident.DetentionDate.Value.Add(DetentionEnd)) },
        Description = $"Reason: {incident.Incident}\nIncident date: {incident.Date.ToString("d/M/yyyy")} {incident.Period}\nSubject: {incident.Subject}\nStaff: {incident.Staff}",
        Reminders = new Event.RemindersData
        {
          UseDefault = false,
          Overrides = new List<EventReminder> {
             new EventReminder {
               Minutes = 10,
               Method = NOTIFY_POPUP
             }
           }
        },
        ColorId = COLOUR_RED
      };

      await Service.Events.Insert(ev, PRIMARY_CALENDAR).ExecuteAsync();
    }

    private async Task DeleteDetentionAsync(DateTime date)
    {
      var listRequest = Service.Events.List(PRIMARY_CALENDAR);
      listRequest.TimeMin = date.Add(DetentionStart).AddMinutes(-61);
      listRequest.TimeMax = date.Add(DetentionEnd).AddMinutes(60);
      var events = await listRequest.ExecuteAsync();
      var ev = events.Items.FirstOrDefault(o => o.Summary == DETENTION_TITLE);
      if (ev == null) return;

      await Service.Events.Delete(PRIMARY_CALENDAR, ev.Id).ExecuteAsync();
    }


    public static async Task UpdateDetentionsAsync(IList<BehaviourIncident> incidents, string key, TimeSpan detentionStart, TimeSpan detentionEnd, string recipientOverride)
    {
      foreach (var student in incidents.GroupBy(o => o.StudentEmail))
      {
        await Task.Delay(250);
        StudentCalendar cal = null;
        try {
          cal = new StudentCalendar(student.Key, key, detentionStart, detentionEnd, recipientOverride);
          foreach (var incident in student)
          {
            if (incident.PreviousDetentionDate != null)
            {              
              await cal.DeleteDetentionAsync(incident.PreviousDetentionDate.Value);
            }
            if (incident.Status == Status.Cancel)
            {
              await cal.DeleteDetentionAsync(incident.DetentionDate.Value);
            }
            else if (incident.EmailRequired)
            {
              await cal.AddDetentionAsync(incident);
            }
          }
        }
        catch
        { }
        finally
        {
          if (cal != null)
          {
            cal.Dispose();
          }
        }
      }
    }


    private static string ConvertToDateTimeRaw(DateTime date)
    {
      var suffix = tzi.IsDaylightSavingTime(date) ? "+01:00" : "Z";
      return date.ToString("yyyy-M-dTHH:mm:ss") + suffix;
    }

    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing)
    {
      if (!disposedValue)
      {
        if (disposing)
        {
          Service.Dispose();
        }
        disposedValue = true;
      }
    }

    public void Dispose()
    {
      Dispose(true);
    }
  }
}