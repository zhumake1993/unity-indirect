using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public unsafe interface IndirectPass
    {
        public void Dispose();
        public void Prepare(IndirectRenderUnmanaged* _unmanaged);
        public void BuildCommandBuffer(CommandBuffer cmd);
    }
}
