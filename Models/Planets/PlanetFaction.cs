﻿using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Models.Planets
{
    public class PlanetFaction
    {
        public Faction Faction { get; }
        public bool IsPublic { get; set; }
        public long Population { get; }
        public int PDFMembers { get; }
        public float PlayerReputation { get; set; }
        public int PlanetaryControl { get; set; }
        public Character Leader { get; set; }

        public PlanetFaction(Faction faction)
        {
            Faction = faction;
            IsPublic = true;
            Population = 0;
            PDFMembers = 0;
            PlayerReputation = 0;
            PlanetaryControl = 0;
        }
    }
}
