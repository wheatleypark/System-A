using System;
using System.Collections.Generic;
using System.Reflection;

namespace WpsBehaviour.Behaviour
{
  public class Tracker
  {
    public DateTime LastExported { get; set; }
    public int StartRow { get; set; }
    public IList<PropertyInfo> ExpectationColumns { get; set; }
    public IList<PropertyInfo> IncidentColumns { get; set; }
  }
}