using UnityEngine;

namespace Nightborn.Interaction
{
    /// <summary>可交互物的统一接口：玩家按 E 触发。</summary>
    public interface IInteractable
    {
        bool CanInteract { get; }
        string Prompt { get; }
        void Interact(GameObject actor);
    }
}
