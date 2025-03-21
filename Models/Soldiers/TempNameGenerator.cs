﻿using OnlyWar.Helpers;

namespace OnlyWar
{
    public static class TempNameGenerator
    {
        private static readonly string[] _names =
        {
            "Abel",
            "Abronio",
            "Abronius",
            "Aburio",
            "Aburius",
            "Accio",
            "Accius",
            "Acilio",
            "Acilius",
            "Aconio",
            "Aconius",
            "Actorio",
            "Actorius",
            "Acutio",
            "Acutius",
            "Aderson",
            "Agorio",
            "Agorius",
            "Agrippa",
            "Alajos",
            "Albinio",
            "Albinius",
            "Albio",
            "Ablius",
            "Alberec",
            "Alenso",
            "Alessio",
            "Alexis",
            "Allectio",
            "Allectius",
            "Amafinio",
            "Amafinius",
            "Amandus",
            "Amatio",
            "Amatius",
            "Amulio",
            "Amulius",
            "Ancus",
            "Anval",
            "Anzio",
            "Aphael",
            "Apollo",
            "Appius",
            "Armand",
            "Armaros",
            "Arvann",
            "Astoric",
            "Attio",
            "Attius",
            "Aulus",
            "Aurellian",
            "Balthus",
            "Belial",
            "Belloch",
            "Boreale",
            "Borgio",
            "Brant",
            "Cadulon",
            "Caeles",
            "Caeso",
            "Caius",
            "Calas",
            "Camillo",
            "Camillus",
            "Canio",
            "Canus",
            "Castigon",
            "Consultus",
            "Corien",
            "Cortez",
            "Cossio",
            "Cossus",
            "Courbray",
            "Crixus",
            "Cules",
            "D'Arquebus",
            "Daed",
            "Dantalion",
            "Darius",
            "Darnath",
            "Davian",
            "Decimo",
            "Decimus",
            "Decio",
            "Decius",
            "Diomedes",
            "Donatos",
            "Draco",
            "Drusio",
            "Drusus",
            "Elam",
            "Elias",
            "Erasmus",
            "Fafnir",
            "Faustus",
            "Fidelis",
            "Flavio",
            "Flavius",
            "Folkert",
            "Furioso",
            "Gaius",
            "Galedan",
            "Gallio",
            "Gallus",
            "Garro",
            "Gessart",
            "Gnaeus",
            "Grimmer",
            "Grulgor",
            "Grummer",
            "Heilbron",
            "Herio",
            "Herius",
            "Hostio",
            "Hostus",
            "Ignatius",
            "Indrick",
            "Iscon",
            "Julius",
            "Kaeso",
            "Kaesoron",
            "Karcunio",
            "Karcunus",
            "Karlaen",
            "Kerith",
            "Kolak",
            "Larce",
            "Laris",
            "Larth",
            "Larsus",
            "Leandro",
            "Leitz",
            "Leonatos",
            "Lexandro",
            "Lucius",
            "Lysander",
            "Machio",
            "Machiavi",
            "Mamercus",
            "Manio",
            "Manius",
            "Marcello",
            "Marcellus",
            "Marcio",
            "Marcus",
            "Marius",
            "Melian",
            "Mettio",
            "Mettius",
            "Minato",
            "Minatus",
            "Minio",
            "Minius",
            "Moriar",
            "Morleo",
            "Nadael",
            "Narth",
            "Nathaniel",
            "Nerio",
            "Nerius",
            "Nero",
            "Nerus",
            "Nonio",
            "Nonus",
            "Novio",
            "Novius",
            "Numerius",
            "Numio",
            "Numus",
            "Obiareus",
            "Octavius",
            "Odovocar",
            "Opiter",
            "Oriax",
            "Orlandra",
            "Ovio",
            "Ovius",
            "Paccio",
            "Paccius",
            "Paullus",
            "Pavel",
            "Phaeton",
            "Pheus",
            "Phobor",
            "Pollux",
            "Polux",
            "Pompo",
            "Portan",
            "Postumus",
            "Proculus",
            "Publius",
            "Puplio",
            "Puplius",
            "Quentus",
            "Quintus",
            "Quirion",
            "Rann",
            "Raxiatel",
            "Reinhart",
            "Reus",
            "Roac",
            "Sable",
            "Sammael",
            "Salvio",
            "Salvius",
            "Saul",
            "Sendini",
            "Sendroth",
            "Seppio",
            "Seppius",
            "Septimus",
            "Sertor",
            "Servio",
            "Servius",
            "Sethreus",
            "Sextus",
            "Sien",
            "Sigismund",
            "Sigmund",
            "Silas",
            "Slayne",
            "Solomon",
            "Soron",
            "Spurio",
            "Spurius",
            "Startio",
            "Startius",
            "Statio",
            "Statius",
            "Stern",
            "Sumatris",
            "Taelos",
            "Tain",
            "Taltos",
            "Tane",
            "Tawn",
            "Taremar",
            "Tarnus",
            "Tarvitz",
            "Taurio",
            "Taurus",
            "Telamon",
            "Thawn",
            "Thule",
            "Tiberio",
            "Tiberius",
            "Titus",
            "Tragan",
            "Trebio",
            "Trebius",
            "Tullio",
            "Tullus",
            "Tyr",
            "Vairosean",
            "Valafar",
            "Vale",
            "Vettio",
            "Vettius",
            "Vibio",
            "Vibius",
            "Vilius",
            "Voleo",
            "Volero",
            "Volesus",
            "Vopio",
            "Vopiscus",
            "Wayn",
            "Xander",
            "Zander",
            "Zedrenael",
            "Zorael",

        };

        public static string GetName()
        {
            int nameCount = _names.Length;
            int firstNameNumber = RNG.GetIntBelowMax(0, nameCount);
            return _names[firstNameNumber];
        }
    }
}
