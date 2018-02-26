using Google.Apis.Sheets.v4.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using static Google.Apis.Sheets.v4.SpreadsheetsResource;

namespace WpsBehaviour.Behaviour
{
  class BehaviourSpreadsheet : Spreadsheet
  {
    public Tracker Tracker { get; private set; }
    public BehaviourSpreadsheet(string spreadsheetId, string key) : base(spreadsheetId, key) { }

    private int? _expectationsSheetId = null;

    private async Task<int> GetExpectationsSheetId()
    {
      if (_expectationsSheetId == null)
      {
        _expectationsSheetId = (await Execute(Resource.Get(SpreadsheetId))).Sheets.First(o => o.Properties.Title == SheetNames.Expectations).Properties.SheetId;
      }
      return _expectationsSheetId.Value;
    }

    public async Task GetTrackerDataAsync()
    {
      var sheets = (await GetSheetsAsync(SheetNames.AppData, $"{SheetNames.Expectations}!1:1", $"{SheetNames.Incidents}!1:1"));
      var appData = sheets[0];
      if (appData.Values == null || appData.Values.Count != 2 || !DateTime.TryParse(appData.Values[0][0].ToString(), out var lastExported) || !int.TryParse(appData.Values[1][0].ToString(), out var startRow)) {
        throw new InvalidOperationException($"Tracker data invalid. Check {SheetNames.AppData} tab.");
      }
      Tracker = new Tracker {
        LastExported = lastExported,
        StartRow = Math.Max(1, startRow - 1),
        ExpectationColumns = sheets[1].Values[0].ToPropertyList(),
        IncidentColumns = sheets[2].Values[0].ToPropertyList()
      };
    }


    public async Task WriteTrackerDataAsync(int startRow)
    {
      var data = new ValueRange { Values = new[] { new object[] { DateTime.Today.ToString("d-MMM-yy") }, new object[] { startRow } } };

      var request = Resource.Values.Update(data, SpreadsheetId, SheetNames.AppData);
      request.ValueInputOption = ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

      await Execute(request);
    }


    public async Task AddIncidentsAsync(string sheetName, IList<BehaviourIncident> incidents)
    {
      if (incidents.Count == 0) return;

      IList<PropertyInfo> columns = null;
      if (sheetName == SheetNames.Expectations) {
        columns = Tracker.ExpectationColumns;
      } else if (sheetName == SheetNames.Incidents) {
        columns = Tracker.IncidentColumns;
      } else {
        throw new ArgumentException($"Cannot write incidents to sheet '{sheetName}'.", nameof(sheetName));
      }

      var data = new ValueRange { Values = new List<IList<object>>() };

      foreach (var incident in incidents) {
        data.Values.Add(columns.Select(o => o == null ? string.Empty : PropertyMapper.DisplayValue(o.GetValue(incident), o.Name)).ToList());
      }

      var request = Resource.Values.Append(data, SpreadsheetId, sheetName);
      request.ValueInputOption = ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
      request.InsertDataOption = ValuesResource.AppendRequest.InsertDataOptionEnum.OVERWRITE;

      await Execute(request);
    }


    public async Task DeleteIncidentsAsync(IList<BehaviourIncident> incidents)
    {
      if (incidents.Count == 0) return;
      var rowsToDelete = incidents.Select(o => o.SpreadsheetRow.Value).OrderByDescending(o => o);
      var sheetId = await GetExpectationsSheetId();

      var requestBody = new BatchUpdateSpreadsheetRequest {
        Requests = rowsToDelete.Select(o => new Request {
          DeleteDimension = new DeleteDimensionRequest {
            Range = new DimensionRange {
              SheetId = sheetId,
              Dimension = DIMENSION_ROWS,
              StartIndex = o - 1, // Zero-based index
              EndIndex = o // Exclusive
            }
          }
        }).ToList()
      };

      var request = Resource.BatchUpdate(requestBody, SpreadsheetId);
      await Execute(request);
    }

    public async Task SortExpectationsSheet(IList<PropertyInfo> headers)
    {
      var sheetId = await GetExpectationsSheetId();

      var requestBody = new BatchUpdateSpreadsheetRequest
      {
        Requests = new List<Request>
        {
          new Request
          {
            SortRange = new SortRangeRequest
            {
              Range = new GridRange
              {
                SheetId = sheetId,
                StartRowIndex = 1
              },
              SortSpecs = new List<SortSpec>() {
                new SortSpec
                {
                  DimensionIndex = headers.IndexOf(headers.First(o => o.Name == "Status")),
                  SortOrder = "DESCENDING"
                },
                new SortSpec
                {
                  DimensionIndex = headers.IndexOf(headers.First(o => o.Name == "DetentionDate")),
                  SortOrder = "ASCENDING"
                },
                new SortSpec
                {
                  DimensionIndex = headers.IndexOf(headers.First(o => o.Name == "Year")),
                  SortOrder = "ASCENDING"
                },
                new SortSpec
                {
                  DimensionIndex = headers.IndexOf(headers.First(o => o.Name == "Surname")),
                  SortOrder = "ASCENDING"
                },
                new SortSpec
                {
                  DimensionIndex = headers.IndexOf(headers.First(o => o.Name == "Forename")),
                  SortOrder = "ASCENDING"
                }
              }
            }
          }
        }
      };

      var request = Resource.BatchUpdate(requestBody, SpreadsheetId);
      await Execute(request);
    }

    public async Task UpdateDetentionsAsync(IList<BehaviourIncident> incidents)
    {
      if (incidents.Count == 0) return;

      var data = new ValueRange { Values = Enumerable.Range(0, incidents.Max(o => o.SpreadsheetRow ?? 0) - Tracker.StartRow + 1).Select(o => new object[2]).ToArray() };

      const int CHAR_A = 65;
      var detentionCol = Tracker.ExpectationColumns.IndexOf(Tracker.ExpectationColumns.Single(o => o?.Name == nameof(BehaviourIncident.DetentionDate)));
      var range = $"{SheetNames.Expectations}!{Convert.ToChar(detentionCol + CHAR_A).ToString()}{Tracker.StartRow}:{Convert.ToChar(detentionCol + CHAR_A + 1).ToString()}{Tracker.StartRow + data.Values.Count}";

      foreach (var incident in incidents) {
        var row = new object[2];
        row[0] = incident.DetentionDate.Value.ToString("d-MMM-yy");
        row[1] = incident.Status == Status.Pending ? string.Empty : incident.Status.ToString();
        data.Values[incident.SpreadsheetRow.Value - Tracker.StartRow] = row;
      }

      var request = Resource.Values.Update(data, SpreadsheetId, range);
      request.ValueInputOption = ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

      await Execute(request);
    }
  }
}