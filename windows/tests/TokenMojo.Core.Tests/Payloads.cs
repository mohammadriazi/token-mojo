namespace TokenMojo.Core.Tests;

internal static class Payloads
{
    // Shape from code.claude.com/docs/en/statusline, including fields TokenMojo ignores.
    public const string Full = """
        {
          "hook_event_name": "Status",
          "session_id": "abc-123",
          "session_name": "my-session",
          "transcript_path": "/home/alice/.claude/projects/x/abc-123.jsonl",
          "model": { "id": "claude-opus-5-5", "display_name": "Opus" },
          "workspace": { "current_dir": "/home/alice/proj", "project_dir": "/home/alice/proj" },
          "cost": { "total_cost_usd": 1.2345, "total_duration_ms": 45000, "total_lines_added": 10, "total_lines_removed": 2 },
          "context_window": {
            "total_input_tokens": 1000, "total_output_tokens": 200, "context_window_size": 200000,
            "used_percentage": 8, "remaining_percentage": 92,
            "current_usage": { "input_tokens": 10, "output_tokens": 5 }
          },
          "exceeds_200k_tokens": false,
          "rate_limits": {
            "five_hour": { "used_percentage": 23.5, "resets_at": 1738425600 },
            "seven_day": { "used_percentage": 41.2, "resets_at": 1738857600 }
          }
        }
        """;

    public static string ForSession(string id, double cost = 0) =>
        $$$"""{"session_id":"{{{id}}}","cost":{"total_cost_usd":{{{cost.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}""";
}
