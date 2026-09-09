using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Runtime.Naming;
using System;
using System.Collections.Generic;

namespace OnlyWar.Generation.World
{
    public static class CharacterBuilder
    {
        public static Character GenerateCharacter(int id, Faction faction, NameGenerator nameGenerator)
        {
            ArgumentNullException.ThrowIfNull(nameGenerator);
            return new Character()
            {
                Id = id,
                Loyalty = faction,
                Age = RNG.GetIntBelowMax(30, 100),
                Name = nameGenerator.GetFullName(),
                Appreciation = (float)RNG.GetLinearDouble(),
                Influence = (float)RNG.GetLinearDouble(),
                Investigation = (float)RNG.GetLinearDouble(),
                Competence = (float)RNG.GetLinearDouble(),
                Severity = (float)RNG.GetLinearDouble(),
                Neediness = (float)RNG.GetLinearDouble(),
                Paranoia = (float)RNG.GetLinearDouble(),
                Patience = (float)RNG.GetLinearDouble(),
                OpinionOfPlayerForce = (float)RNG.GetLinearDouble(),
                OpinionOfSoldier = [],
                ActiveRequest = null
            };
        }
    }
}
