using System;

namespace PushStars.Core
{
    /// <summary>Brawl-style default nicknames ("TurboWalrus") so new players are not all "PLAYER".
    /// Derived from the uid, so the app and the server (functions/src/playerNames.js) agree on it
    /// without a round trip. Keep both word lists and the hash identical.</summary>
    public static class PlayerNames
    {
        private static readonly string[] Adjectives =
        {
            "Swift", "Brave", "Turbo", "Mighty", "Sneaky", "Lucky", "Fuzzy", "Iron",
            "Golden", "Spicy", "Cosmic", "Wild", "Silent", "Rapid", "Jolly", "Grumpy",
            "Shiny", "Crazy", "Fierce", "Sleepy", "Hyper", "Mega", "Super", "Epic",
            "Funky", "Salty", "Frosty", "Blazing", "Stormy", "Neon", "Atomic", "Royal",
            "Savage", "Chill", "Bouncy", "Crispy", "Dizzy", "Giant", "Happy", "Lazy",
            "Noble", "Pixel", "Quick", "Rusty", "Steel", "Sunny", "Thunder", "Tiny",
            "Ultra", "Wacky", "Zesty", "Bold", "Clever", "Daring", "Fancy", "Heavy",
            "Jumpy", "Loud", "Magic", "Proud", "Sly", "Toxic", "Witty", "Zippy",
        };
        private static readonly string[] Nouns =
        {
            "Tiger", "Panda", "Walrus", "Potato", "Falcon", "Shark", "Rhino", "Badger",
            "Gorilla", "Penguin", "Llama", "Taco", "Cactus", "Dragon", "Wizard", "Pirate",
            "Viking", "Robot", "Comet", "Rocket", "Meteor", "Golem", "Yeti", "Kraken",
            "Phoenix", "Moose", "Otter", "Raccoon", "Hamster", "Bison", "Cobra", "Wolf",
            "Bear", "Eagle", "Fox", "Hippo", "Koala", "Lion", "Mammoth", "Monkey",
            "Octopus", "Parrot", "Pickle", "Muffin", "Noodle", "Pretzel", "Waffle", "Donut",
            "Burrito", "Nugget", "Banana", "Coconut", "Pumpkin", "Cupcake", "Samurai", "Knight",
            "Titan", "Goblin", "Bandit", "Ranger", "Champ", "Beast", "Ninja", "Mango",
        };

        /// <summary>The generated nickname for a uid (or any other stable seed).</summary>
        public static string For(string seed)
        {
            // FNV-1a over UTF-16 code units, as the server's JavaScript walks the string.
            uint h = 2166136261;
            foreach (char c in seed ?? "") h = (h ^ c) * 16777619;
            return Adjectives[h % (uint)Adjectives.Length] + Nouns[h / (uint)Adjectives.Length % (uint)Nouns.Length];
        }

        /// <summary>Names the app or the server used before there were generated ones.</summary>
        public static bool IsPlaceholder(string name)
        {
            string n = (name ?? "").Trim();
            return n.Length == 0 || string.Equals(n, "Player", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, "Игрок", StringComparison.OrdinalIgnoreCase);
        }
    }
}
