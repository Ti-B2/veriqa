// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Globalization;

namespace Veriqa.Sample.DotNet.Inproc.AgentApproval;

/// <summary>Arguments of one tool call, by name.</summary>
public sealed class ToolArguments : Dictionary<string, string>
{
    /// <summary>Creates empty arguments.</summary>
    public ToolArguments()
        : base(StringComparer.Ordinal)
    {
    }
}

/// <summary>
/// The approval policy of a tool: which declared action type asks the person, and how the arguments
/// of the call become the caller slots of that action type. The person sees the slots, never text the
/// agent wrote — so the agent cannot word the question to its own advantage.
/// </summary>
/// <param name="ActionType">Action type declared in the MessageTemplates of the client entry.</param>
/// <param name="Slots">Maps the arguments of a call to the slot values.</param>
public sealed record ApprovalPolicy(string ActionType, Func<ToolArguments, IReadOnlyDictionary<string, string>> Slots);

/// <summary>A tool the agent may call.</summary>
/// <param name="Name">Name the agent calls it by.</param>
/// <param name="Approval">Approval policy; null — the tool runs without asking.</param>
/// <param name="Execute">What the tool does.</param>
public sealed record AgentTool(
    string Name,
    ApprovalPolicy? Approval,
    Func<ToolArguments, CancellationToken, Task<string>> Execute);

/// <summary>One order of the in-memory shop.</summary>
/// <param name="Id">Order number.</param>
/// <param name="Item">What was bought.</param>
/// <param name="Amount">What was paid.</param>
/// <param name="Customer">E-mail of the customer.</param>
public sealed record Order(string Id, string Item, string Amount, string Customer);

/// <summary>
/// The tools of the support agent. Known in advance, as in any agent a team builds for one job: each
/// tool is plain code, and the ones with consequences carry an approval policy.
/// </summary>
public sealed class SupportTools
{
    private readonly ConcurrentDictionary<string, Order> _orders = new(StringComparer.Ordinal)
    {
        ["A-1042"] = new("A-1042", "Wireless headphones", "89.00 EUR", "anna@example.com"),
        ["A-1043"] = new("A-1043", "Standing desk", "420.00 EUR", "ben@example.com")
    };

    private readonly ConcurrentDictionary<string, bool> _refunded = new(StringComparer.Ordinal);

    /// <summary>The tools, by name.</summary>
    public IReadOnlyDictionary<string, AgentTool> All { get; }

    /// <summary>Builds the tool set.</summary>
    public SupportTools()
    {
        AgentTool[] tools =
        [
            // Reading is harmless: no approval.
            new("lookup_order", null, (args, _) => Task.FromResult(
                _orders.TryGetValue(args["order"], out var order)
                    ? $"Order {order.Id}: {order.Item}, paid {order.Amount}, customer {order.Customer}, "
                        + (_refunded.ContainsKey(order.Id) ? "already refunded." : "not refunded.")
                    : $"Order {args["order"]} does not exist.")),

            // Moving money is not: a person approves every call, with the amount and the order in front of them.
            new(
                "refund_payment",
                new ApprovalPolicy("approve-refund", args => new Dictionary<string, string>
                {
                    ["amount"] = args["amount"],
                    ["order"] = args["order"],
                    ["reason"] = args["reason"]
                }),
                (args, _) =>
                {
                    _refunded[args["order"]] = true;
                    return Task.FromResult($"Refunded {args["amount"]} for order {args["order"]}.");
                }),

            // Writing to the customer about a decision already taken: no approval in this sample.
            new("send_customer_email", null, (args, _) => Task.FromResult(
                $"E-mail sent to the customer of order {args["order"]}: \"{args["text"]}\"")),
        ];

        All = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
    }
}

/// <summary>What a person decided about one tool call.</summary>
/// <param name="Approved">True only for a confirmed outcome.</param>
/// <param name="Outcome">Outcome of the confirmation.</param>
/// <param name="DecidedBy">Channel identity of the person who confirmed; null otherwise.</param>
public sealed record ApprovalDecision(bool Approved, string Outcome, string? DecidedBy);

/// <summary>
/// The human-in-the-loop gate: turns a tool call into a Veriqa confirmation and waits for the person.
/// It sits in the tool-invocation path, not in the prompt, so no output of the model can skip it.
/// </summary>
public sealed class ApprovalGate(VeriqaConfirmationClient veriqa)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Asks for approval of one call and waits for the outcome. The wait ends by itself: a transaction
    /// nobody answers ends as expired after its TTL.
    /// </summary>
    public async Task<ApprovalDecision> RequestAsync(
        AgentRun run,
        AgentTool tool,
        ToolArguments arguments,
        CancellationToken cancellationToken)
    {
        var policy = tool.Approval!;
        var created = await veriqa.CreateAsync(policy.ActionType, policy.Slots(arguments), cancellationToken);

        run.AwaitApproval(new PendingApproval(tool.Name, created.ChannelEntry.Url, created.ChannelEntry.Qr));

        try
        {
            while (true)
            {
                await Task.Delay(PollInterval, cancellationToken);
                var result = await veriqa.GetResultAsync(created.TransactionId, cancellationToken);

                if (result.Outcome == "pending")
                {
                    continue;
                }

                if (result.Outcome != VeriqaConfirmationClient.ConfirmedOutcome)
                {
                    return new ApprovalDecision(false, result.Outcome, null);
                }

                // Who approved goes into the agent's own log — the audit of the run.
                var claims = await veriqa.ExchangeForIdTokenClaimsAsync(created.TransactionId, cancellationToken);
                return new ApprovalDecision(true, result.Outcome, claims.GetProperty("sub").GetString());
            }
        }
        finally
        {
            run.AwaitApproval(null);
        }
    }
}

/// <summary>
/// The support agent. Where a real agent asks a model which tool to call next, this one follows a fixed
/// plan, so the sample runs without a model and without an API key. The part that matters does not
/// depend on who chose the call: every call goes through <see cref="InvokeAsync"/>, and that is where
/// the approval is enforced.
/// </summary>
public sealed class SupportAgent(SupportTools tools, ApprovalGate gate)
{
    /// <summary>Handles one refund request from a customer.</summary>
    public async Task RunAsync(AgentRun run, RefundRequest request, CancellationToken cancellationToken)
    {
        try
        {
            run.Log("agent", $"Ticket: the customer asks to refund order {request.Order} — \"{request.Reason}\".");

            await InvokeAsync(run, "lookup_order", new ToolArguments { ["order"] = request.Order }, cancellationToken);

            var refund = await InvokeAsync(
                run,
                "refund_payment",
                new ToolArguments { ["order"] = request.Order, ["amount"] = request.Amount, ["reason"] = request.Reason },
                cancellationToken);

            var text = refund.Executed
                ? $"Your refund of {request.Amount} is on its way."
                : "We could not approve a refund right now; a colleague will contact you.";

            await InvokeAsync(
                run,
                "send_customer_email",
                new ToolArguments { ["order"] = request.Order, ["text"] = text },
                cancellationToken);

            run.Complete("Done.");
        }
        catch (Exception failure) when (failure is VeriqaCallException or HttpRequestException)
        {
            run.Complete($"Stopped: {failure.Message}");
        }
    }

    /// <summary>
    /// Calls one tool. A tool with an approval policy runs only after a person confirmed the call; any
    /// other outcome returns to the agent as the result of the call, the way a harness reports a refusal
    /// to the model.
    /// </summary>
    private async Task<(bool Executed, string Result)> InvokeAsync(
        AgentRun run,
        string toolName,
        ToolArguments arguments,
        CancellationToken cancellationToken)
    {
        var tool = tools.All[toolName];
        run.Log("call", $"{tool.Name}({string.Join(", ", arguments.Select(pair => $"{pair.Key}: {pair.Value}"))})");

        if (tool.Approval is not null)
        {
            run.Log("approval", $"{tool.Name} needs a person's approval — waiting.");
            var decision = await gate.RequestAsync(run, tool, arguments, cancellationToken);

            if (!decision.Approved)
            {
                var refusal = $"Not executed: the approval ended as {decision.Outcome}.";
                run.Log("result", refusal);
                return (false, refusal);
            }

            run.Log("approval", $"Approved by {decision.DecidedBy}.");
        }

        var result = await tool.Execute(arguments, cancellationToken);
        run.Log("result", result);

        return (true, result);
    }
}

/// <summary>What the page asks the agent to handle.</summary>
/// <param name="Order">Order number.</param>
/// <param name="Amount">Amount to refund.</param>
/// <param name="Reason">What the customer wrote.</param>
public sealed record RefundRequest(string Order, string Amount, string Reason);

/// <summary>The way in of an approval the run is waiting for.</summary>
/// <param name="Tool">The tool waiting.</param>
/// <param name="Url">Deep link of the confirmation.</param>
/// <param name="Qr">The same address as a PNG data URI.</param>
public sealed record PendingApproval(string Tool, string Url, string? Qr);

/// <summary>One line of the run log.</summary>
/// <param name="At">When it was written.</param>
/// <param name="Kind">agent, call, approval or result.</param>
/// <param name="Text">The line.</param>
public sealed record AgentLogEntry(string At, string Kind, string Text);

/// <summary>The state of one run, read by the page while the agent works.</summary>
public sealed class AgentRun
{
    private readonly Lock _gate = new();
    private readonly List<AgentLogEntry> _log = [];
    private PendingApproval? _pending;
    private string? _completion;

    /// <summary>Identifier of the run.</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Appends a line to the log.</summary>
    public void Log(string kind, string text)
    {
        lock (_gate)
        {
            _log.Add(new AgentLogEntry(DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), kind, text));
        }
    }

    /// <summary>Sets or clears the approval the run is waiting for.</summary>
    public void AwaitApproval(PendingApproval? pending)
    {
        lock (_gate)
        {
            _pending = pending;
        }
    }

    /// <summary>Marks the run finished.</summary>
    public void Complete(string text)
    {
        lock (_gate)
        {
            _pending = null;
            _completion = text;
        }
    }

    /// <summary>The state for the page.</summary>
    public object Snapshot()
    {
        lock (_gate)
        {
            return new { id = Id, log = _log.ToArray(), pending = _pending, completion = _completion };
        }
    }
}
