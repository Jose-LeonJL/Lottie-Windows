﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Toolkit.Uwp.UI.Lottie.UIData.CodeGen.AbstractSyntax
{
    /// <summary>
    /// A reference to something that can be called.
    /// </summary>
    abstract class CallTargetReference
    {
        protected internal CallTargetReference(TypeReference resultType)
        {
            ResultType = resultType;
        }

        public TypeReference ResultType { get; }
    }
}
