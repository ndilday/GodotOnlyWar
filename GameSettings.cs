using System;

using OnlyWar.Models;
using OnlyWar.Helpers;

[Serializable]
public class GameSettings
{
    public bool debugMode;
    public int SectorSize;
    public Tuple<ushort, ushort> MapScale;
    public Tuple<ushort, ushort> BattleMapScale;
    public Sector Sector;
    public PlayerForce Chapter;
    public Date Date;

    //[Header("SharedData")]
    //public bool IsDialogShowing;
}