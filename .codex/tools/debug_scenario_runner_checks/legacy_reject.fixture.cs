// Historical negative-control fixture; never compiled into the game.
        private static string Reject(Report report, string status, string reason)
        { report.status = status; report.automatedStatus = status; report.reason = reason; report.phase = "Finished"; report.finishedUtc = DateTime.UtcNow.ToString("O"); Save(report); SessionState.SetString(LastKey, report.reportPath); return report.reportPath; }
