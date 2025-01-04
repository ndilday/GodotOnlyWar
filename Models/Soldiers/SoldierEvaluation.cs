
namespace OnlyWar.Models.Soldiers
{
    public class SoldierEvaluation
    {
        public float MeleeRating { get; }
        public float RangedRating { get; }
        public float LeadershipRating { get; }
        public float MedicalRating { get; }
        public float TechRating { get; }
        public float PietyRating { get; }
        public float AncientRating { get; }
        //public ISoldier Evaluator { get; }
        public Date EvaluationDate { get; }

        public SoldierEvaluation(Date evaluationDate, float melee, float ranged, float lead, float med, float tech, float piety, float ancient)
        {
            //Evaluator = evaluator;
            EvaluationDate = evaluationDate;
            MeleeRating = melee;
            RangedRating = ranged;
            LeadershipRating = lead;
            MedicalRating = med;
            TechRating = tech;
            PietyRating = piety;
            AncientRating = ancient;
        }
    }
}
