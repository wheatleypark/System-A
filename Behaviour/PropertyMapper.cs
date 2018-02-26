using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WpsBehaviour.Behaviour
{
  static class PropertyMapper
  {
    public static object DisplayValue(object val, string column, bool longDate = false)
    {
      if (val == null) return null;
      switch (column) {
        case "Date":
        case "DetentionDate":
        case "PreviousDetentionDate":
          return ((DateTime)val).ToString(longDate ? "dddd d MMMM yyyy" : "d-MMM-yy");
        case "Status":
          var status = (Status)val;
          return status == Status.Pending ? string.Empty : status.ToString();
        default:
          return val;
      }
    }


    public static object InterpretValue(string val, Type type)
    {
      if (type == typeof(DateTime)) {
        return (DateTime.TryParse(val, out var date)) ? date : default;
      }
      if (type == typeof(DateTime?)) {
        return (DateTime.TryParse(val, out var date)) ? date : (DateTime?)null;
      }
      if (type == typeof(Status)) {
        if (val == string.Empty) return Status.Pending;
        return Enum.Parse(typeof(Status), val);
      }
      return val;
    }


    public static IList<PropertyInfo> ToPropertyList(this IList<object> columnHeaders)
    {
      return columnHeaders.Select(o => typeof(BehaviourIncident).GetProperty(o.ToString())).ToList();
    }
  }
}