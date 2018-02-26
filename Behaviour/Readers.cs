using Google.Apis.Sheets.v4.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WpsBehaviour.Behaviour
{
  static class Readers
  {

    public static (IList<BehaviourIncident>, int startRow) ReadUnresolved(ValueRange valueRange, IList<PropertyInfo> columns, int startRow)
    {
      var unresolvedIncidents = new List<BehaviourIncident>();
      var newStartRow = startRow;

      for (var r = 1; r < valueRange.Values.Count; r++) { // Skip first row because we fetched (startRow - 1)
        var row = valueRange.Values[r];
        var incident = new BehaviourIncident {
          SpreadsheetRow = startRow + r
        };
        for (var c = 0; c < row.Count; c++)
        {
          if (c >= columns.Count || columns[c] == null)
          {
            continue;
          }
          columns[c].SetValue(incident, PropertyMapper.InterpretValue(row[c].ToString(), columns[c].PropertyType));
        }
        if (incident.Status != Status.Resolved)
        {
          unresolvedIncidents.Add(incident);
        }
        if (incident.Status == Status.Resolved || incident.Status == Status.Escalate)
        {
          newStartRow++;
        }
      }
      return (unresolvedIncidents, newStartRow);
    }


    public static IDictionary<String, String> ReadContacts(ValueRange range)
      => range.Values.ToDictionary(o => o[0].ToString(), o => o[1].ToString());


    public static IList<DateTime> ReadDetentionDays(ValueRange range)
      => range.Values.Select(o => DateTime.Parse(o[0].ToString())).Where(o => o > DateTime.Today).OrderBy(o => o).ToList();


    public static IList<EmailTemplate> ReadTemplates(ValueRange range)
      => range.Values.Select(o => new EmailTemplate { Subject = o[0].ToString(), Body = o[1].ToString() }).ToList();

  }
}