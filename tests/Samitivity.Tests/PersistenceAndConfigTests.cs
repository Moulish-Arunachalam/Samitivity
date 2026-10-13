using Samitivity.Core;

namespace Samitivity.Tests;
public sealed class PersistenceAndConfigTests : TempTest
{
    private AtomicJsonStore<AppConfig> Store => new(Path.Combine(Root, "config.json"), AppConfig.Defaults, x => x.Validate());
    [Fact] public void CorruptPrimaryRecoversValidBackup()
    {
        var first = AppConfig.Defaults(); Store.Save(first); Store.Save(first with { Categories = [] });
        File.WriteAllText(Path.Combine(Root, "config.json"), "{broken");
        var result = Store.Load(); Assert.NotNull(result.Warning); Assert.Equal(3, result.Value.Categories.Count);
        Store.Save(result.Value); File.WriteAllText(Path.Combine(Root, "config.json"), "broken again");
        Assert.Equal(3, Store.Load().Value.Categories.Count);
    }
    [Fact] public void MalformedSchemaAndBothInvalidFilesUseDefaults()
    {
        File.WriteAllText(Path.Combine(Root, "config.json"), "{\"Categories\":null}");
        File.WriteAllText(Path.Combine(Root, "config.json.bak"), "[]");
        Assert.Equal(3, Store.Load().Value.Categories.Count); Assert.NotNull(Store.Load().Warning);
    }
    [Fact] public void PartialTempWriteIsIgnoredAndAtomicWritesLeaveNoTemporaryFiles()
    {
        Store.Save(AppConfig.Defaults()); File.WriteAllText(Path.Combine(Root, "config.json.tmp-interrupted"), "{\"Version\":");
        Assert.Null(Store.Load().Warning); Store.Save(AppConfig.Defaults());
        Assert.Single(Directory.GetFiles(Root, "*.tmp-*"));
    }
    [Fact] public void InvalidSaveDoesNotReplaceValidConfiguration()
    {
        Store.Save(AppConfig.Defaults());
        Assert.Throws<InvalidDataException>(() => Store.Save(new AppConfig { StopShortcut = new(Modifiers.None, 0x53) }));
        Assert.Equal(3, Store.Load().Value.Categories.Count);
    }
    [Fact] public void RenamingPreservesIdentityAndSupportsNewCategory()
    {
        var config = AppConfig.Defaults(); var id = config.Categories[0].Id;
        config.Categories[0] = config.Categories[0] with { Name = "A new name" };
        config.Categories.Add(new(Guid.NewGuid(), "Fourth", new(Modifiers.Control | Modifiers.Shift, 0x41)));
        Store.Save(config); Assert.Equal(id, Store.Load().Value.Categories[0].Id); Assert.Equal(4, Store.Load().Value.Categories.Count);
    }
    [Fact] public void DuplicateShortcutsAndNamesRejected()
    {
        var config = AppConfig.Defaults(); config.Categories[0] = config.Categories[0] with { Shortcut = config.StopShortcut };
        Assert.Throws<InvalidDataException>(config.Validate);
        config = AppConfig.Defaults(); config.Categories[0] = config.Categories[0] with { Name = config.Categories[1].Name };
        Assert.Throws<InvalidDataException>(config.Validate);
    }
    [Theory] [InlineData(0, 83)] [InlineData(2, 123)] [InlineData(3, 46)] [InlineData(16, 83)] [InlineData(2, 192)]
    public void UnsafeAndUnsupportedShortcutsRejected(int modifiers, int key) => Assert.Throws<InvalidDataException>(() => new Shortcut((Modifiers)modifiers, key).Validate());
    [Fact] public void EmergencyStopAllowsModifierBackspaceAndModifierDelete()
    {
        new Shortcut(Modifiers.Control | Modifiers.Alt, 8).Validate();
        new Shortcut(Modifiers.Control | Modifiers.Shift, 46).Validate();
    }
    [Fact] public void VersionOneConfigurationRemainsCompatibleWithoutMigration()
    {
        var path = Path.Combine(Root, "config.json");
        File.WriteAllText(path, """
        {"Version":1,"Categories":[{"Id":"05c22a8f-210d-4dab-bc9a-116a9c236101","Name":"Personal name","Shortcut":{"Modifiers":3,"Key":83}}],"StopShortcut":{"Modifiers":3,"Key":8}}
        """);
        var original = Store.Load(); Assert.Null(original.Warning); Store.Save(original.Value);
        Assert.Equal(1, Store.Load().Value.Version); Assert.Equal("Personal name", Assert.Single(Store.Load().Value.Categories).Name);
        Assert.Equal(new Shortcut(Modifiers.Control | Modifiers.Alt, 8), Store.Load().Value.StopShortcut);
    }
    [Fact] public void SemanticallyCorruptStateRecovers()
    {
        var path = Path.Combine(Root, "state.json"); File.WriteAllText(path, "{\"Categories\":null}");
        Assert.NotNull(Engine().RecoveryWarning); Assert.NotNull(Engine().Select(Guid.NewGuid(), ["a"]));
    }
}
