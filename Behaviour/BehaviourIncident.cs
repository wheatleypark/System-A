using System;
using System.Linq;

namespace WpsBehaviour.Behaviour
{
  public class BehaviourIncident
  {
    public DateTime Date { get; set; }
    public string Forename { get; set; }
    public string Reg { get; set; }
    public string Incident { get; set; }
    public string Period { get; set; }
    public string Class { get; set; }
    public string Subject { get; set; }
    public string Time { get; set; }
    public string Location { get; set; }
    public string Comments { get; set; }
    public string Gender { get; set; }
    public string SEN { get; set; }
    public string StudentEmail { get; set; }
    public string ParentEmail { get; set; }
    public DateTime? DetentionDate { get; set; }
    public Status Status { get; set; }
    public int? SpreadsheetRow { get; set; }
    public bool IsChanged { get; set; }
    public DateTime? PreviousDetentionDate { get; set; }
    public bool EmailRequired { get; set; }

    private string _pp = null;
    public string PP {
      get {
        return _pp;
      }
      set {
        _pp = (value == "T") ? "T" : string.Empty;
      }
    }

    private string _surname = null;
    public string Surname
    {
      get
      {
        return _surname;
      }
      set
      {
        _surname = value?.ToUpper();
      }
    }

    private string _staff = null;
    public string Staff
    {
      get
      {
        return _staff;
      }
      set
      {
        _staff = (string.IsNullOrEmpty(value) && Incident.Contains("Chromebook")) ? "IT Support" : value;
      }
    }

    private string _parentSalutation = null;
    public string ParentSalutation
    {
      get
      {
        return string.IsNullOrEmpty(ParentEmail) ? string.Empty : _parentSalutation;
      }
      set
      {
        _parentSalutation = string.IsNullOrEmpty(value) ? "Parent/Carer" : value.ToTitleCase();
      }
    }

    private string _year;
    public string Year {
      get {
        if (_year == null) {
          _year = new string(Reg.TakeWhile(o => char.IsDigit(o) || o == '/').ToArray());
        }
        return _year;
      }
      set {
        _year = value;
      }
    }

  }

  public enum Status { Pending, Resolved, Escalate, Reschedule, Cancel, Overdue }
}
