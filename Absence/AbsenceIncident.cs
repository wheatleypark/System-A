using System;

namespace WpsBehaviour.Absence
{
  public class AbsenceIncident
  {
    private static string REGISTRATION = "_Registration";
    public static string ANYLESSONS = "_AtLeastOneLesson";

    public string Forename { get; set; }
    public string Surname { get; set; }
    public string Reg { get; set; }
    public string StudentEmail { get; set; }
    public string RegistrationMark { get; set; }
    public string ParentSalutation { get; set; }
    public string ParentEmail { get; set; }
    public string Class { get; set; }
    public string Subject { get; set; }
    public string Teacher { get; set; }
    public string Date { get { return DateTime.Today.ToString("d-MMM-yy"); } }
    public string Student { get { return $"{Surname}, {Forename}"; } }
    public string Missed { get { return (RegistrationMark != null) ? REGISTRATION : Subject; } }
  }
}
