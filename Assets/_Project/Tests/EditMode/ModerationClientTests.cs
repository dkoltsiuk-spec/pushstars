using NUnit.Framework;
using PushStars.Services;
using UnityEngine;

namespace PushStars.Tests
{
    public class ModerationClientTests
    {
        private const string Key = "moderation.blocked.v1";
        private string _saved;

        [SetUp] public void SetUp() { _saved = PlayerPrefs.GetString(Key, ""); PlayerPrefs.DeleteKey(Key); }
        [TearDown] public void TearDown() { if (_saved == "") PlayerPrefs.DeleteKey(Key); else PlayerPrefs.SetString(Key, _saved); }

        [Test]
        public void ABlockedBotIsInTheListWithItsName()
        {
            string id = new string('a', 32);
            ModerationClient.SetBlocked(null, id, true, "LucasFit").GetAwaiter().GetResult();
            try
            {
                Assert.IsTrue(ModerationClient.IsBlocked(id));
                var listed = System.Array.Find(ModerationClient.LocalPlayers(), p => p.botId == id);
                Assert.IsNotNull(listed);
                Assert.AreEqual("LucasFit", listed.name);
                Assert.IsEmpty(listed.uid);
            }
            finally { ModerationClient.SetBlocked(null, id, false).GetAwaiter().GetResult(); }
        }

        [Test]
        public void UnblockingABotTakesItOutOfTheList()
        {
            string id = new string('b', 32);
            ModerationClient.SetBlocked(null, id, true, "Rafa_10").GetAwaiter().GetResult();
            ModerationClient.SetBlocked(null, id, false).GetAwaiter().GetResult();
            Assert.IsFalse(ModerationClient.IsBlocked(id));
            Assert.IsNull(System.Array.Find(ModerationClient.LocalPlayers(), p => p.botId == id));
        }
    }
}
