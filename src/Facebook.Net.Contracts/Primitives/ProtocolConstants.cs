// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Facebook.Net.Contracts.Primitives;

/// <summary>
/// Central registry for Comet protocol constants, endpoints, headers, and reaction IDs.
/// </summary>
public static partial class ProtocolConstants
{
    /// <summary>
    /// Core protocol headers, tokens, and defaults.
    /// </summary>
    public static class Protocol
    {
        /// <summary>Fixed application branch identifier for Comet desktop web (x-asbd-id).</summary>
        public const string AsbdId = "359341";

        /// <summary>Default package identifier for Comet desktop web (__pc).</summary>
        public const string DefaultPackage = "EXP2:comet_pkg";

        /// <summary>Default connection quality class (__ccg).</summary>
        public const string DefaultConnectionClass = "EXCELLENT";

        /// <summary>Default main branch identifier (__spin_b).</summary>
        public const string DefaultSpinBranch = "trunk";

        /// <summary>Anti-JSONP prefix prepended by ajax endpoints.</summary>
        public const string AntiJsonpPrefix = "for (;;);";

        /// <summary>Default staggered delay in milliseconds between browser profile launches (10 seconds).</summary>
        public const int DefaultStaggeredLaunchDelayMs = 10_000;

        /// <summary>Default pagination delay in milliseconds between cursor fetches (2 seconds).</summary>
        public const int DefaultPaginationDelayMs = 2_000;

        /// <summary>Default fallback RUpload service host name for video uploads.</summary>
        public const string DefaultVideoUploadService = "rupload-sin6-3.up";

        /// <summary>Fixed Watch video home section node identifier (Base64 of "vh:1838196319817896").</summary>
        public const string WatchHomeSectionId = "dmg6MTgzODE5NjMxOTgxNzg5Ng==";

        /// <summary>Default preset ID for plain text stories.</summary>
        public const string DefaultStoryTextPresetId = "2173297922999264";

        /// <summary>Default User-Agent string mimicking modern Chromium on Windows.</summary>
        public const string DefaultWindowsUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36";

        /// <summary>Default User-Agent string mimicking modern Chromium on macOS.</summary>
        public const string DefaultMacOsUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36";
    }

    /// <summary>
    /// Standard web and API endpoints.
    /// </summary>
    public static class Endpoints
    {
        /// <summary>Base origin URL for desktop web.</summary>
        public const string BaseOrigin = "https://www.facebook.com";

        /// <summary>Home URL with English locale for deterministic token extraction.</summary>
        public const string HomeEnglishUrl = "https://www.facebook.com/?locale=en_EN";

        /// <summary>Primary GraphQL execution endpoint.</summary>
        public const string GraphQl = "https://www.facebook.com/api/graphql/";

        /// <summary>Photo upload endpoint for feed posts and comments.</summary>
        public const string UfiPhotoUpload = "https://www.facebook.com/ajax/ufi/upload/";

        /// <summary>Photo upload endpoint for stories.</summary>
        public const string StoryPhotoUpload = "https://upload.facebook.com/ajax/react_composer/attachments/photo/upload";

        /// <summary>Video upload session initialization endpoint.</summary>
        public const string VideoUploadStart = "https://www.facebook.com/ajax/video/upload/requests/start/";

        /// <summary>Video upload session completion endpoint.</summary>
        public const string VideoUploadReceive = "https://www.facebook.com/ajax/video/upload/requests/receive/";
    }

    /// <summary>
    /// Fixed feedback reaction identifiers for UFI mutations.
    /// </summary>
    public static class Reactions
    {
        /// <summary>Like reaction ID (👍).</summary>
        public const string Like = "1635855486666999";

        /// <summary>Love reaction ID (❤️).</summary>
        public const string Love = "1678524932434102";

        /// <summary>Care reaction ID (🥰).</summary>
        public const string Care = "613557422527858";

        /// <summary>Haha reaction ID (😆).</summary>
        public const string Haha = "115940658764963";

        /// <summary>Wow reaction ID (😮).</summary>
        public const string Wow = "478547315650144";

        /// <summary>Sad reaction ID (😢).</summary>
        public const string Sad = "908563459236466";

        /// <summary>Angry reaction ID (😡).</summary>
        public const string Angry = "1845773152337158";
    }
}
