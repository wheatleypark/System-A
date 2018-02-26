using System;
using System.Collections.Generic;
using System.Linq;

namespace WpsBehaviour.Absence
{
  public class StudentAbsence
  {
    public string Forename { get; set; }
    public string Surname { get; set; }
    public string Reg { get; set; }
    public string StudentEmail { get; set; }
    public string ParentEmail { get; set; }
    public string Date { get { return DateTime.Today.ToString("d-MMM-yy"); } }
    public IList<SessionAbsence> MissedSessions { get; set; }

    public string MissedSessionBullets {
      get {
        return "<ul>" + string.Concat(MissedSessions.Select(o => $"<li>{o}</li>")) + "</ul>";
      }
    }

    public string MissedLessonBullets {
      get {
        return "<br />" + string.Concat(MissedSessions.Where(o => o.Class != null).Select(o => $"&nbsp; * {o}<br />")) + "<br />";
      }
    }


    private string _parentSalutation;
    public string ParentSalutation {
      get {
        if (string.IsNullOrEmpty(_parentSalutation)) {
          return "Parent/Carer";
        }
        return _parentSalutation;
      }
      set {
        _parentSalutation = value;
      }
    }

    public class SessionAbsence
    {
      public string Session { get; set; }
      public string Class { get; set; }
      public string Teacher { get; set; }

      public override string ToString()
      {
        var rtn = Session;
        if (!string.IsNullOrEmpty(Teacher)) {
          rtn += $" - {Teacher}";
        }
        return rtn;
      }
    }
  }
}