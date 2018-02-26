using Newtonsoft.Json;
using System;
using System.Globalization;
using System.IO;

namespace WpsBehaviour
{
  public class Config
  {
    private static volatile Config instance;
    private static object syncRoot = new object();

    public const string CONFIG_PATH = "..\\config.json";
    public const string KEY_PATH = "..\\key.json";

    public const string APP_NAME = "WheatleyPark";
    public string BehaviourSpreadsheetId { get; set; }
    public string AbsenceSpreadsheetId { get; set; }
    public string SenderEmail { get; set; }
    public string SenderPassword { get; set; }
    public TimeSpan DetentionStartTimeSpan { get; private set; }
    public TimeSpan DetentionEndTimeSpan { get; private set; }
    public string Key { get; set; }

    private string _debugRecipientEmail = null;
    public string DebugRecipientEmail {
      get {
        return _debugRecipientEmail;
      }
      set {
#if DEBUG
        _debugRecipientEmail = value;
#else
        // Uncomment for remote debug
        //_debugRecipientEmail = value;
#endif
      }
    }

    public string DetentionStart {
      set {
        DetentionStartTimeSpan = TimeSpan.ParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture);
      }
    }


    public string DetentionEnd {
      set {
        DetentionEndTimeSpan = TimeSpan.ParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture);
      }
    }

    public static Config Instance(string functionRootPath)
    {
      if (instance == null) {
        lock (syncRoot) {
          if (instance == null) {
            instance = JsonConvert.DeserializeObject<Config>(File.ReadAllText(Path.Combine(functionRootPath, CONFIG_PATH)));
            instance.Key = File.ReadAllText(Path.Combine(functionRootPath, KEY_PATH));
          }
        }
      }
      return instance;
    }
  }
}