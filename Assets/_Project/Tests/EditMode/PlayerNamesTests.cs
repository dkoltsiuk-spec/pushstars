using NUnit.Framework;
using PushStars.Core;

namespace PushStars.Tests
{
    public class PlayerNamesTests
    {
        [Test]
        public void SameNamesAsTheServer()
        {
            // functions/test/playerNames.test.js pins the same samples.
            Assert.AreEqual("LuckyOctopus", PlayerNames.For("abc123"));
            Assert.AreEqual("WittyLion", PlayerNames.For("Zr8kQ2mPx9TfYw1LhN3s"));
            Assert.AreEqual("LuckyKnight", PlayerNames.For(""));
        }

        [Test]
        public void PlaceholdersAreNotNames()
        {
            foreach (var name in new[] { null, "", " ", "PLAYER", "Player", "Игрок" })
                Assert.IsTrue(PlayerNames.IsPlaceholder(name), name);
            Assert.IsFalse(PlayerNames.IsPlaceholder("Дана ★"));
        }
    }
}
