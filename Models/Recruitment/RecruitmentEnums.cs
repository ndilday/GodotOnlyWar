namespace OnlyWar.Models.Recruitment
{
    public enum RecruitmentPolicy
    {
        VoluntaryPresentation = 0,
        PlanetaryTithe = 1
    }

    public enum RecruitmentStaffRole
    {
        ScoutSergeant = 0,
        Apothecary = 1,
        Chaplain = 2
    }

    public enum RecruitmentWorldType
    {
        Standard = 0,
        Feral = 1,
        Death = 2
    }

    public enum RecruitmentPhase
    {
        Phase0PreImplantation = 0,
        Phase1 = 1,
        Phase2 = 2,
        Phase3 = 3,
        Phase4 = 4,
        Phase5 = 5,
        Phase6 = 6,
        Phase7 = 7,
        Phase8 = 8,
        Phase9 = 9,
        Phase10 = 10,
        Phase11 = 11,
        Phase12 = 12,
        Phase13BlackCarapace = 13
    }

    public enum RecruitmentProcedureType
    {
        Implantation = 0,
        BlackCarapace = 1
    }

    public enum RecruitmentProcedureStatus
    {
        Pending = 0,
        InProgress = 1,
        Paused = 2
    }

    public enum RecruitmentEventType
    {
        ProgramEstablished = 0,
        CandidateQualified = 1,
        CandidateAgedOut = 2,
        AspirantAdmitted = 3,
        ImplantationCompleted = 4,
        AspirantDied = 5,
        NeophytePromoted = 6,
        BlackCarapaceCompleted = 7,
        BattleBrotherPromoted = 8,
        ProgramPaused = 9
    }
}
