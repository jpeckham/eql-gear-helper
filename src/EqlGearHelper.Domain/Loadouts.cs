namespace EqlGearHelper.Domain;

public sealed record LoadoutAssignment(EquipmentPosition Position, OwnedItemInstance Item, CatalogItem? ItemDefinition = null);

public sealed record Loadout
{
    public Loadout(ClassTrio trio, IReadOnlyList<LoadoutAssignment> assignments)
    {
        Trio = trio ?? throw new ArgumentNullException(nameof(trio));
        Assignments = Array.AsReadOnly((assignments ?? throw new ArgumentNullException(nameof(assignments))).ToArray());

        var duplicateInstances = Assignments
            .GroupBy(assignment => assignment.Item.InstanceId)
            .Any(group => group.Count() > 1);
        if (duplicateInstances)
        {
            throw new ArgumentException("A physical item copy cannot fill multiple positions.", nameof(assignments));
        }
    }

    public ClassTrio Trio { get; }
    public IReadOnlyList<LoadoutAssignment> Assignments { get; }
}
