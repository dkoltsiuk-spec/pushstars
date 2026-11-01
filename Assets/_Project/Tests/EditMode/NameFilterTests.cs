using NUnit.Framework;
using PushStars.Core;

namespace PushStars.Tests
{
    public class NameFilterTests
    {
        // functions/test/nameFilter.test.js pins the same samples.
        private static readonly string[] Refused =
        {
            "fuck", "FuCk You", "fuuuck", "f_u_c_k", "fu-ck", "fuсk", "sh1t", "B1tch", "nigga", "BigAss", "a s s",
            "Хуй", "хуйло", "пиздец", "Сука", "с у к а", "cyka", "suka", "pidor", "Мудак", "ёбаный", "блядь",
            "caralho", "PUTA", "filho da puta", "Hitler", "x1488x", "admin", "Moderator1", "Pushup Stars", "Apple", "Official",
        };
        private static readonly string[] Allowed =
        {
            "Dana", "Дана", "Denis_07", "Class", "Spicy", "Peacock", "Raccoon", "Lebanon", "Knight", "Computador",
            "Рубль", "Корабль", "Сабля", "João", "Lucas Fit", "IronBadger", "dimon_push", "LucasFit", "SwiftOtter", "Katya_K",
            "joao_br", "MAKS2009", "tigr_ufa", "Rafa_10",
        };

        [Test]
        public void ObjectionableNamesAreRefused()
        {
            foreach (var name in Refused) Assert.IsTrue(NameFilter.IsObjectionable(name), name);
        }

        [Test]
        public void OrdinaryNamesPass()
        {
            foreach (var name in Allowed) Assert.IsFalse(NameFilter.IsObjectionable(name), name);
        }

        [Test]
        public void GeneratedNamesPass()
        {
            for (int i = 0; i < 2000; i++)
            {
                string name = PlayerNames.For("uid-" + i);
                Assert.IsFalse(NameFilter.IsObjectionable(name), name);
            }
        }

        [Test]
        public void APeersWordIsNotTakenForIt()
        {
            Assert.AreEqual("Dana", NameFilter.Shown(" Dana ", "u1"));
            Assert.AreEqual(PlayerNames.For("u1"), NameFilter.Shown("Сука", "u1"));
            Assert.AreEqual(PlayerNames.For("u1"), NameFilter.Shown("<b>Dana</b>", "u1"));
            Assert.AreEqual(PlayerNames.For("u1"), NameFilter.Shown("", "u1"));
            Assert.AreEqual(PlayerNames.For("u1"), NameFilter.Shown("Da\uD800na", "u1"));
        }
    }
}
