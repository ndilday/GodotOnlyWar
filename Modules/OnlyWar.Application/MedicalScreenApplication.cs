using System;
using OnlyWar.Helpers;

namespace OnlyWar.Application;

public sealed class MedicalScreenApplication : CampaignScreenApplication,
    IMedicalScreenApplication
{
    private readonly RecoveryOperationsViewModelBuilder _emptyRecoveryViews = new();

    private MedicalReadContext Read => Context.MedicalRead;
    private MedicalCommandContext Commands => Context.MedicalCommand;

    public MedicalScreenApplication(CampaignApplicationContext context) : base(context) { }

    public MedicalScreenView QueryMedical(MedicalScreenQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        MedicalReadResult result = Read?.BuildMedical(query);
        return result == null
            ? new(SessionToken, [], null, null, null)
            : new(SessionToken, result.Tree, result.Vault, result.Rollup, result.Soldier);
    }

    public RecoveryScreenView QueryRecovery(RecoveryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        MedicalReadContext read = Read;
        RecoveryOperationsViewModel model = read?.BuildRecovery(query)
            ?? _emptyRecoveryViews.Build(
                null,
                [],
                query.SoldierId,
                query.Sort,
                query.Ascending,
                null,
                query.Movement,
                query.HitLocationId,
                query.ProcedureType);
        return new(SessionToken, model);
    }

    public RecoveryPlanCommitResult ConfirmRecovery(ConfirmRecoveryCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        MedicalReadContext read = Read;
        if (read == null || command.SessionToken != SessionToken)
            return new(false, "The campaign changed. Review the current recovery plan.");
        return Commands.ConfirmRecovery(command);
    }
}
