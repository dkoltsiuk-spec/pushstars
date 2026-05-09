using Cysharp.Threading.Tasks;

namespace PushStars.Core
{
    public interface IInitializable
    {
        UniTask InitializeAsync();
    }
}
