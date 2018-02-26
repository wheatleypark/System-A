using System;
using System.Collections.Generic;
using System.Reflection;

namespace WpsBehaviour.Absence
{
  public class AppData
  {
    public DateTime LastExported { get; set; }
    public IList<PropertyInfo> AbsencesColumns { get; set; }
    public IDictionary<string, string> Contacts { get; set; }
    public IList<EmailTemplate> EmailTemplates { get; set; }
  }
}