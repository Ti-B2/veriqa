// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace Veriqa.Sample.DotNet.Inproc.StepUp;

/// <summary>One project of the workspace.</summary>
/// <param name="Id">Identifier used in the URLs.</param>
/// <param name="Name">Name shown in the confirmation.</param>
/// <param name="Documents">Number of documents deleted with it.</param>
public sealed record Project(string Id, string Name, int Documents);

/// <summary>What the backend decided about a deletion once the confirmation ended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeletionVerdict
{
    /// <summary>Declined, expired or failed: nothing was deleted.</summary>
    NotDeleted,

    /// <summary>Confirmed by the owner the confirmation expected: the project is deleted.</summary>
    Deleted,

    /// <summary>No owner was known yet: the person who confirmed became the owner, and the project is deleted.</summary>
    DeletedOwnerBound,

    /// <summary>Confirmed, but not by the expected owner: nothing was deleted.</summary>
    ConfirmedByAnotherPerson
}

/// <summary>A deletion waiting for its confirmation, or already decided.</summary>
/// <param name="TransactionId">The confirmation transaction.</param>
/// <param name="ProjectId">The project it deletes.</param>
/// <param name="Outcome">Outcome of the confirmation: pending until it ends.</param>
/// <param name="Verdict">What the backend did; null while pending.</param>
public sealed record Deletion(string TransactionId, string ProjectId, string Outcome, DeletionVerdict? Verdict);

/// <summary>
/// The in-memory state of the sample: the projects, the owner of the account, and the deletions in
/// flight. In a real application this is the database, and the owner's channel identity comes from
/// account linking rather than from the first confirmation.
/// </summary>
public sealed class ProjectWorkspace
{
    /// <summary>
    /// Comparable identity type the owner is expected under — declared for the client in
    /// IdentityMatchComparableTypes (appsettings.json).
    /// </summary>
    public const string OwnerIdentityType = "telegram_user_id";

    private readonly Lock _gate = new();
    private readonly List<Project> _projects =
    [
        new("q3-report", "Q3 report", 12),
        new("customer-onboarding", "Customer onboarding", 48),
        new("archive-2024", "Archive 2024", 305)
    ];
    private readonly Dictionary<string, Deletion> _deletions = new(StringComparer.Ordinal);

    /// <summary>Channel user id of the owner; null until the first confirmed deletion.</summary>
    public string? Owner { get; private set; }

    /// <summary>The projects and the owner, for the page.</summary>
    public object Snapshot()
    {
        lock (_gate)
        {
            return new { owner = Owner, projects = _projects.ToArray() };
        }
    }

    /// <summary>A project by id, or null.</summary>
    public Project? Find(string projectId)
    {
        lock (_gate)
        {
            return _projects.Find(project => project.Id == projectId);
        }
    }

    /// <summary>Remembers which project a confirmation transaction deletes.</summary>
    public void StartDeletion(string transactionId, string projectId)
    {
        lock (_gate)
        {
            _deletions[transactionId] = new Deletion(transactionId, projectId, "pending", null);
        }
    }

    /// <summary>A deletion by its transaction, or null.</summary>
    public Deletion? FindDeletion(string transactionId)
    {
        lock (_gate)
        {
            return _deletions.GetValueOrDefault(transactionId);
        }
    }

    /// <summary>The first person to confirm becomes the owner; a later call changes nothing.</summary>
    public void BindOwner(string channelUserId)
    {
        lock (_gate)
        {
            Owner ??= channelUserId;
        }
    }

    /// <summary>Forgets the owner, so the next confirmed deletion binds a new one.</summary>
    public void ResetOwner()
    {
        lock (_gate)
        {
            Owner = null;
        }
    }

    /// <summary>
    /// Records the verdict of a deletion once, and deletes the project when the verdict says so.
    /// A deletion already decided keeps its first verdict.
    /// </summary>
    public Deletion Conclude(string transactionId, string outcome, DeletionVerdict verdict)
    {
        lock (_gate)
        {
            var deletion = _deletions[transactionId];
            if (deletion.Verdict is not null)
            {
                return deletion;
            }

            if (verdict is DeletionVerdict.Deleted or DeletionVerdict.DeletedOwnerBound)
            {
                _projects.RemoveAll(project => project.Id == deletion.ProjectId);
            }

            deletion = deletion with { Outcome = outcome, Verdict = verdict };
            _deletions[transactionId] = deletion;

            return deletion;
        }
    }
}
