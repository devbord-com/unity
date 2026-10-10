# devbord reports for Unity

One C# file that sends bug reports from your game to devbord. Every report becomes a card with a screenshot as cover,
the game version, the platform, the scene, the device and the latest log lines.

## Install

1. In devbord, create an **API key with the "Report only" scope** on the board for bugs and pick the list the reports go
   to (board menu › Integrations › API keys). Such a key can only submit reports and reads nothing, so it may ship
   inside the game. This needs the Standard plan or higher: Standard includes 50 reports a month, Premium has no monthly limit.
2. Copy `DevbordReports.cs` into the `Assets` folder of your project.

## Use

```csharp
using Devbord;

void Awake()
{
    DevbordReports.Url = "https://devbord.com";
    DevbordReports.ApiKey = "db_xxxxxxxx_...";
    DevbordReports.CaptureLogs(); // reports then carry the latest log lines
}

public void OnReportButton(string text)
{
    var report = new DevbordReports.Report
    {
        Description = text,
        ReporterRef = playerId,          // your own reference to the player
        ReporterName = playerName,
        Fingerprint = null,              // set it to collect the same bug on one card
    };
    report.Fields["Level"] = currentLevel;
    StartCoroutine(DevbordReports.Send(report, result =>
    {
        if (result.ok) PlayerPrefs.SetString("lastReport", result.receipt);
        else Debug.LogWarning("Report failed: " + result.code + " " + result.error);
    }));
}

// later, for example at startup: was it fixed?
StartCoroutine(DevbordReports.Check(PlayerPrefs.GetString("lastReport"), status =>
{
    if (status != null && status.state == "completed") ShowToast("Your bug report #" + status.number + " was fixed.");
}));
```

## Notes

- A key inside a game can be extracted by players. That is what the "Report only" scope is for: the worst someone can
  do with it is submit reports, up to the limit of the key (120 per hour by default, adjustable on the key). Never ship
  a key with read or write access.
- Add your own cooldown in the interface so one player cannot use up the hourly limit.
- For crashes and exceptions without a player typing anything, use the error tracking of devbord instead (it speaks
  the protocol of the Sentry SDK for Unity).
- This file has not been compiled inside Unity yet; it targets Unity 2019.4 and newer. Try it in the editor first.
