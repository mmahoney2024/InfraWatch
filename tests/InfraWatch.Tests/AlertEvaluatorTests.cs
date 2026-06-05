using InfraWatch.Core;
using InfraWatch.Engine;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraWatch.Tests;

public class AlertEvaluatorTests
{
    [Fact]
    public async Task Alerts_only_on_transition_into_and_out_of_Critical()
    {
        var channel = new CapturingChannel();
        var evaluator = new AlertEvaluator([channel], NullLogger<AlertEvaluator>.Instance);

        await evaluator.EvaluateAsync([Rec(HealthStatus.Healthy)], default);
        Assert.Empty(channel.Sent);                          // healthy -> nothing

        await evaluator.EvaluateAsync([Rec(HealthStatus.Critical)], default);
        Assert.Single(channel.Sent);                         // entered critical -> 1 alert
        Assert.Equal(HealthStatus.Critical, channel.Sent[^1].Severity);

        await evaluator.EvaluateAsync([Rec(HealthStatus.Critical)], default);
        Assert.Single(channel.Sent);                         // still critical -> no new alert

        await evaluator.EvaluateAsync([Rec(HealthStatus.Healthy)], default);
        Assert.Equal(2, channel.Sent.Count);                 // recovered -> recovery alert
        Assert.Equal(HealthStatus.Healthy, channel.Sent[^1].Severity);
    }

    [Fact]
    public async Task Seed_suppresses_alert_for_preexisting_critical()
    {
        var channel = new CapturingChannel();
        var evaluator = new AlertEvaluator([channel], NullLogger<AlertEvaluator>.Instance);

        evaluator.Seed([Rec(HealthStatus.Critical)]);        // e.g. loaded from store at startup
        await evaluator.EvaluateAsync([Rec(HealthStatus.Critical)], default);

        Assert.Empty(channel.Sent);                          // no re-alert on restart
    }

    [Fact]
    public async Task No_channels_is_a_safe_noop()
    {
        var evaluator = new AlertEvaluator([], NullLogger<AlertEvaluator>.Instance);
        await evaluator.EvaluateAsync([Rec(HealthStatus.Critical)], default);
        Assert.False(evaluator.HasChannels);
    }

    private static HealthRecord Rec(HealthStatus status) => new()
    {
        Pillar = "Jira", Target = "timeclock", Check = "open-unaddressed",
        Status = status, Summary = "6 open timeclock ticket(s)",
    };

    private sealed class CapturingChannel : IAlertChannel
    {
        public List<Alert> Sent { get; } = [];
        public string Name => "test";
        public Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
        {
            Sent.Add(alert);
            return Task.CompletedTask;
        }
    }
}
