// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Facebook.Net.Contracts.Primitives;

public static partial class ProtocolConstants
{
    /// <summary>
    /// Persisted GraphQL query identifiers (<c>doc_id</c>) and friendly names.
    /// </summary>
    public static class Queries
    {
        /// <summary>People You May Know suggestions query.</summary>
        public const string PymkSuggestionsDocId = "9917809191634193";

        /// <summary>People You May Know friendly name.</summary>
        public const string PymkSuggestionsName = "FriendingCometPYMKPanelPaginationQuery";

        /// <summary>Incoming friend requests query.</summary>
        public const string IncomingFriendRequestsDocId = "24062887186648527";

        /// <summary>Incoming friend requests friendly name.</summary>
        public const string IncomingFriendRequestsName = "FriendingCometFriendRequestsSectionPanelPaginationQuery";

        /// <summary>Outgoing friend requests query.</summary>
        public const string OutgoingFriendRequestsDocId = "9776114965832879";

        /// <summary>Outgoing friend requests friendly name.</summary>
        public const string OutgoingFriendRequestsName = "FriendingCometOutgoingRequestsDialogPaginationQuery";

        /// <summary>Profile followers list pagination query.</summary>
        public const string ProfileFollowersDocId = "24526674566935562";

        /// <summary>Profile followers friendly name.</summary>
        public const string ProfileFollowersName = "ProfileCometAppCollectionListRendererPaginationQuery";

        /// <summary>Send friend request mutation.</summary>
        public const string FriendRequestSendDocId = "9408332592608561";

        /// <summary>Send friend request friendly name.</summary>
        public const string FriendRequestSendName = "FriendingCometFriendRequestSendMutation";

        /// <summary>Confirm incoming friend request mutation.</summary>
        public const string FriendRequestConfirmDocId = "10003720989745533";

        /// <summary>Confirm incoming friend request friendly name.</summary>
        public const string FriendRequestConfirmName = "FriendingCometFriendRequestConfirmMutation";

        /// <summary>Reject incoming friend request mutation.</summary>
        public const string FriendRequestRejectDocId = "9869125063134847";

        /// <summary>Cancel outgoing friend request mutation.</summary>
        public const string FriendRequestCancelDocId = "7885384641585723";

        /// <summary>Delete/Cancel friend request friendly name.</summary>
        public const string FriendRequestDeleteName = "FriendingCometFriendRequestDeleteMutation";

        /// <summary>Unfriend user mutation.</summary>
        public const string UnfriendDocId = "23930708339886851";

        /// <summary>Unfriend user friendly name.</summary>
        public const string UnfriendName = "FriendingCometUnfriendMutation";

        /// <summary>Unfollow user mutation.</summary>
        public const string UnfollowUserDocId = "9829779950432073";

        /// <summary>Unfollow user friendly name.</summary>
        public const string UnfollowUserName = "CometUserUnfollowMutation";

        /// <summary>Poke user mutation.</summary>
        public const string PokeUserDocId = "29511440545169840";

        /// <summary>Poke user friendly name.</summary>
        public const string PokeUserName = "PokesMutatorPokeMutation";

        /// <summary>Friend lists root query.</summary>
        public const string FriendListsRootDocId = "29800661609524801";

        /// <summary>Friend lists root friendly name.</summary>
        public const string FriendListsRootName = "FriendListCometRootQuery";

        /// <summary>Update friend list members mutation.</summary>
        public const string FriendListUpdateMembersDocId = "9980273685337937";

        /// <summary>Update friend list members friendly name.</summary>
        public const string FriendListUpdateMembersName = "useFriendListCometUpdateMembersMutation";

        /// <summary>Newsfeed pagination query.</summary>
        public const string NewsFeedPaginationDocId = "9957095364411180";

        /// <summary>Newsfeed pagination friendly name.</summary>
        public const string NewsFeedPaginationName = "CometNewsFeedPaginationQuery";

        /// <summary>Single post content query.</summary>
        public const string SinglePostContentDocId = "24002447362717951";

        /// <summary>Single post content friendly name.</summary>
        public const string SinglePostContentName = "CometSinglePostDialogContentQuery";

        /// <summary>Post reaction mutation.</summary>
        public const string PostReactDocId = "9518016021660044";

        /// <summary>Post reaction friendly name.</summary>
        public const string PostReactName = "CometUFIFeedbackReactMutation";

        /// <summary>Create comment mutation.</summary>
        public const string CreateCommentDocId = "9261808033920695";

        /// <summary>Create comment friendly name.</summary>
        public const string CreateCommentName = "useCometUFICreateCommentMutation";

        /// <summary>Post reactors list pagination query.</summary>
        public const string PostReactorsDocId = "9561069197331106";

        /// <summary>Post reactors friendly name.</summary>
        public const string PostReactorsName = "CometUFIReactionsDialogTabContentRefetchQuery";

        /// <summary>Post comments list pagination query.</summary>
        public const string PostCommentsDocId = "9994312660685367";

        /// <summary>Post comments friendly name.</summary>
        public const string PostCommentsName = "CommentsListComponentsPaginationQuery";

        /// <summary>Link attachment preview query prior to sharing.</summary>
        public const string LinkAttachmentPreviewDocId = "24138745772416942";

        /// <summary>Link attachment preview friendly name.</summary>
        public const string LinkAttachmentPreviewName = "ComposerLinkAttachmentPreviewQuery";

        /// <summary>Share story mutation.</summary>
        public const string ShareStoryCreateDocId = "9510250992417785";

        /// <summary>Publish new story/post mutation.</summary>
        public const string PublishStoryCreateDocId = "30414495441498438";

        /// <summary>Story creation friendly name.</summary>
        public const string ComposerStoryCreateName = "ComposerStoryCreateMutation";

        /// <summary>Search groups query.</summary>
        public const string SearchGroupsDocId = "9729744383789355";

        /// <summary>Search results friendly name.</summary>
        public const string SearchGroupsName = "SearchCometResultsPaginatedResultsQuery";

        /// <summary>Joined groups list query.</summary>
        public const string JoinedGroupsDocId = "9676137519162874";

        /// <summary>Joined groups friendly name.</summary>
        public const string JoinedGroupsName = "GroupsCometAllJoinedGroupsSectionPaginationQuery";

        /// <summary>Group feed stories query.</summary>
        public const string GroupFeedDocId = "9755367644572581";

        /// <summary>Group feed stories friendly name.</summary>
        public const string GroupFeedName = "GroupsCometFeedRegularStoriesPaginationQuery";

        /// <summary>Group new members query.</summary>
        public const string GroupNewMembersDocId = "24670246842562940";

        /// <summary>Group new members friendly name.</summary>
        public const string GroupNewMembersName = "GroupsCometMembersPageNewMembersSectionRefetchQuery";

        /// <summary>Join group mutation.</summary>
        public const string GroupJoinDocId = "23945723458372755";

        /// <summary>Join group friendly name.</summary>
        public const string GroupJoinName = "GroupCometJoinForumMutation";

        /// <summary>Group membership questions query.</summary>
        public const string GroupQuestionsDocId = "24850850671182236";

        /// <summary>Group membership questions friendly name.</summary>
        public const string GroupQuestionsName = "GroupsCometMembershipQuestionsDialogQuery";

        /// <summary>Save group membership answers mutation.</summary>
        public const string GroupSaveAnswersDocId = "23951724534439437";

        /// <summary>Save group membership answers friendly name.</summary>
        public const string GroupSaveAnswersName = "useGroupMembershipAnswersSaveMutation";

        /// <summary>Leave group mutation.</summary>
        public const string GroupLeaveDocId = "24471243335909558";

        /// <summary>Leave group friendly name.</summary>
        public const string GroupLeaveName = "GroupCometLeaveForumMutation";

        /// <summary>Invite members to group mutation.</summary>
        public const string GroupAddMembersDocId = "9856173864498377";

        /// <summary>Invite members to group friendly name.</summary>
        public const string GroupAddMembersName = "useGroupAddMembersMutation";

        /// <summary>Stories tray query.</summary>
        public const string StoriesTrayDocId = "9861985363909710";

        /// <summary>Stories tray friendly name.</summary>
        public const string StoriesTrayName = "StoriesTrayRectangularQuery";

        /// <summary>Mark story as seen mutation.</summary>
        public const string StoryMarkSeenDocId = "9567413276713742";

        /// <summary>Mark story as seen friendly name.</summary>
        public const string StoryMarkSeenName = "storiesUpdateSeenStateMutation";

        /// <summary>Send story reply/reaction mutation.</summary>
        public const string StorySendReplyDocId = "9697491553691692";

        /// <summary>Send story reply friendly name.</summary>
        public const string StorySendReplyName = "useStoriesSendReplyMutation";

        /// <summary>Create new story mutation.</summary>
        public const string StoryCreateDocId = "24226878183562473";

        /// <summary>Create new story friendly name.</summary>
        public const string StoryCreateName = "StoriesCreateMutation";

        /// <summary>Watch video feed pagination query.</summary>
        public const string WatchFeedDocId = "10062801637135626";

        /// <summary>Watch video feed friendly name.</summary>
        public const string WatchFeedName = "CometVideoHomeFeedSectionPaginationQuery";

        /// <summary>Notifications list pagination query.</summary>
        public const string NotificationsListDocId = "24270421129216356";

        /// <summary>Notifications list friendly name.</summary>
        public const string NotificationsListName = "CometNotificationsListPaginationQuery";

        /// <summary>Mark notification as read mutation.</summary>
        public const string NotificationMarkReadDocId = "9077759465660685";

        /// <summary>Mark notification as read friendly name.</summary>
        public const string NotificationMarkReadName = "CometNotificationsUpdateSeenStateMutation";

        /// <summary>Managed fanpages list query.</summary>
        public const string ManagedPagesDocId = "29849393258040848";

        /// <summary>Managed fanpages friendly name.</summary>
        public const string ManagedPagesName = "PagesCometLaunchPointUnifiedQueryPagesListRedesignedUpdatedPagesSectionQuery";

        /// <summary>Media upload server configuration retriever query.</summary>
        public const string MediaUploadConfigDocId = "6325794097543029";

        /// <summary>Media upload server configuration friendly name.</summary>
        public const string MediaUploadConfigName = "MediaUploadFBDefaultServerConfigurationRetrieverQuery";
    }
}
