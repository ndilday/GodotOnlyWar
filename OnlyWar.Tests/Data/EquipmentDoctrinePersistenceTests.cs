using Microsoft.Data.Sqlite;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using Xunit;

namespace OnlyWar.Tests.Data;

public sealed class EquipmentDoctrinePersistenceTests
{
    [Fact]
    public void EquipmentDoctrine_RoundTripsRoleAndRosterPersonalLoadouts()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = @"
                CREATE TABLE ChapterEquipmentRoleLoadout (PersonalEquipmentRoleId INTEGER PRIMARY KEY, ArmorEquipmentId INTEGER);
                CREATE TABLE ChapterEquipmentRoleLoadoutItem (PersonalEquipmentRoleId INTEGER, EquipmentId INTEGER, Quantity INTEGER, InitialReadyOrder INTEGER);
                CREATE TABLE Soldier (Id INTEGER PRIMARY KEY);
                CREATE TABLE SoldierEquipmentLoadout (SoldierId INTEGER PRIMARY KEY, ArmorEquipmentId INTEGER);
                CREATE TABLE SoldierEquipmentLoadoutItem (SoldierId INTEGER, EquipmentId INTEGER, Quantity INTEGER, InitialReadyOrder INTEGER);";
            schema.ExecuteNonQuery();
        }
        using (var soldier = connection.CreateCommand())
        {
            soldier.CommandText = "INSERT INTO Soldier (Id) VALUES (99)";
            soldier.ExecuteNonQuery();
        }

        EquipmentTemplate armor = new(
            701,
            "Armor",
            armorProfile: new ArmorProfile(2));
        EquipmentTemplate weapon = new(
            702,
            "Weapon",
            rangedProfile: new RangedWeaponProfile(null));
        IReadOnlyDictionary<int, EquipmentTemplate> equipment =
            new Dictionary<int, EquipmentTemplate> { [armor.Id] = armor, [weapon.Id] = weapon };
        EquipmentLoadout loadout = new(
            armor,
            new EquipmentLoadoutEntry(weapon, 1, 0));
        EquipmentLoadoutDoctrine original = new();
        original.SetRoleDefault(7, loadout);
        original.SetPersonalLoadout(99, loadout);

        LoadoutDoctrineDataAccess access = new();
        using (var transaction = connection.BeginTransaction())
        {
            access.SaveEquipmentDoctrine(transaction, original);
            transaction.Commit();
        }

        EquipmentLoadoutDoctrine restored = access.GetEquipmentDoctrine(
            connection,
            equipment,
            new Dictionary<int, EquipmentKitTemplate>());

        Assert.True(restored.TryGetRoleDefault(7, out EquipmentLoadout roleLoadout));
        Assert.True(restored.TryGetPersonalLoadout(99, out EquipmentLoadout personalLoadout));
        Assert.Equal(loadout.Signature, roleLoadout.Signature);
        Assert.Equal(loadout.Signature, personalLoadout.Signature);
    }
}
