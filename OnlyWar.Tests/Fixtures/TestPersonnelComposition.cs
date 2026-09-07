using System.Runtime.CompilerServices;
using OnlyWar.Contracts.Operations;
using OnlyWar.Helpers.Application.Adapters.Operations;

namespace OnlyWar.Tests.Fixtures;

/// <summary>
/// The test assembly's composition root for the Operations personnel capability. Production
/// composes it in <c>CampaignApplication</c>; without an equivalent here, order, attachment and
/// movement tests that never construct an application or a turn controller fail with
/// "No Operations personnel capability has been composed for this command."
/// </summary>
internal static class TestPersonnelComposition
{
    [ModuleInitializer]
    internal static void Configure() =>
        OperationsPersonnelDefaults.Configure(OperationsPersonnelSurface.Instance);
}
