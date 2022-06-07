// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace ILLink.Shared.DataFlow
{
    public readonly partial struct LocalKey : IEquatable<LocalKey>
    {
        public partial bool Equals (LocalKey other);

        public override bool Equals (object? obj) => obj is LocalKey other && Equals (other);

        public override partial int GetHashCode ();
    }
}