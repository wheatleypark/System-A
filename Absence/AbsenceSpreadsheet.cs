using Google.Apis.Sheets.v4.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Google.Apis.Sheets.v4.SpreadsheetsResource;

namespace WpsBehaviour.Absence
{
  class AbsenceSpreadsheet : Spreadsheet
  {
    public AppData AppData { get; private set; }


    public AbsenceSpreadsheet(string spreadsheetId, string key) : base(spreadsheetId, key) { }
    

    public async Task GetAppDataAsync()
    {
      var sheets = (await GetSheetsAsync(SheetNames.AppData, $"{SheetNames.Absences}!1:1", SheetNames.Contacts, SheetNames.EmailTemplates));
      var appData = sheets[0];
      if (appData.Values == null || !DateTime.TryParse(appData.Values[0][0].ToString(), out var lastExported)) {
        throw new InvalidOperationException($"Tracker data invalid. Check {SheetNames.AppData} tab.");
      }
      AppData = new AppData {
        LastExported = lastExported,
        AbsencesColumns = sheets[1].Values[0].Select(o => typeof(AbsenceIncident).GetProperty(o.ToString())).ToList(),
        Contacts = sheets[2].Values.ToDictionary(o => o[0].ToString(), o => o[1].ToString()),
        EmailTemplates = sheets[3].Values.Select(o => new EmailTemplate { Subject = o[0].ToString(), Body = o[1].ToString() }).ToList()
      };
    }


    public async Task WriteLastEditDateAsync()
    {
      var data = new ValueRange { Values = new[] { new object[] { DateTime.Today.ToString("d-MMM-yy") } } };

      var request = Resource.Values.Update(data, SpreadsheetId, SheetNames.AppData);
      request.ValueInputOption = ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

      await request.ExecuteAsync();
    }


    public async Task AddAbsencesAsync(IList<AbsenceIncident> absences)
    {
      if (absences.Count == 0) return;

      var data = new ValueRange { Values = new List<IList<object>>() };

      foreach (var absence in absences.OrderBy(o => o.Reg).ThenBy(o => o.Student).ThenBy(o => o.Missed)) {
        data.Values.Add(AppData.AbsencesColumns.Select(o => o == null ? string.Empty : o.GetValue(absence)).ToList());
      }

      var request = Resource.Values.Append(data, SpreadsheetId, SheetNames.Absences);
      request.ValueInputOption = ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
      request.InsertDataOption = ValuesResource.AppendRequest.InsertDataOptionEnum.OVERWRITE;

      await request.ExecuteAsync();
    }


  }
}