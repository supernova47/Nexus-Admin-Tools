using System;
using SuperNova.ValheimSpawner;

internal static class Program
{
    private static int assertions;

    private static void Check(bool result, string description)
    {
        assertions++;
        if (!result) throw new Exception("FavoriteIds failed: " + description);
    }

    private static void Main()
    {
        FavoriteIds favorites = new FavoriteIds();
        Check(favorites.Count == 0, "empty initial set");
        Check(favorites.Toggle("ShieldWood"), "first star");
        Check(favorites.Contains("shieldwood"), "case-insensitive lookup");
        Check(!favorites.Toggle("SHIELDWOOD"), "unstar with different casing");
        Check(favorites.Count == 0, "empty after unstar");
        Check(favorites.Toggle("Sword;Rune,Extra"), "identifier containing delimiters");
        Check(favorites.Toggle("Обереги Мод"), "unicode identifier");
        string encoded = favorites.ToEncodedString();
        FavoriteIds reloaded = FavoriteIds.FromEncodedString(encoded);
        Check(reloaded.Count == 2, "round-trip entries");
        Check(reloaded.Contains("Обереги Мод"), "round-trip unicode");
        Check(reloaded.Contains("sword;rune,extra"), "round-trip delimiters");
        Check(reloaded.ToEncodedString() == encoded, "deterministic serialization");
        Check(!reloaded.Toggle(""), "reject empty id");
        Check(!reloaded.Toggle(new string('x', 257)), "reject oversized id");
        Check(!reloaded.Toggle("Invalid\nId"), "reject control characters");
        Check(FavoriteIds.FromEncodedString("bad!," + encoded + ",!bad").Count == 2, "skip invalid tokens");
        Check(FavoriteIds.FromEncodedString(null).Count == 0, "null config accepted");
        Console.WriteLine("FavoriteIds C# smoke tests passed: " + assertions + " assertions.");
    }
}
