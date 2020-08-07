﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Collections.Immutable;
using System.Reflection;

namespace Microsoft.CodeAnalysis.Host.Mef
{
    public static class DesktopMefHostServices
    {
        public static MefHostServices DefaultServices => MefHostServices.DefaultHost;
        public static ImmutableArray<Assembly> DefaultAssemblies => MefHostServices.DefaultAssemblies;
    }
}
