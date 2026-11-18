# IMAP mail bodies and attachments

The fetch block accepts either simple `SearchText` or a standard IMAP
`SearchExpression`, for example `OR TEXT "invoice" TEXT "receipt"`. Filling one
disables the other; annotation validation rejects configurations containing both.
Expressions are sent through MailKit's raw string search API without a custom
parser. Do not include `SEARCH` or `UID SEARCH`. The whole expression is grouped
before adding the block's date and unread filters with AND, preserving the same
date-filter behavior as simple text searches. Empty search fields leave only
those additional filters. Server-specific criteria depend on server support.

The IMAP mail fetch block retrieves message summaries and text body parts, never
the full MIME message or attachment content. Quoted-history cleanup can also read
the HTML alternative to recognize structural quote containers. Original message
output contains the original textual body, not the complete MIME message.

`ImapAttachmentDownloadFlowBlock` uses an associated IMAP fetch block, or resolves
one on its predecessor path. It has no input-field configuration. The source must
expose the current mail's `UniqueId`; exposing `UidValidity` additionally protects
against a mailbox identity change. The block opens the same account and folder
read-only and retrieves attachment parts individually.

Each attachment produces one dataset with its filename and decoded byte content.
Content type and size are optional result fields. Attached messages are returned
as `.eml` files. Inline images are not treated as ordinary attachments.

Attachment filename and byte-content result fields can be passed to compatible
document or table converters. Text extraction and further processing are handled
by separate FlowBlocks.

Verification uses mocked IMAP transfers and document conversion unit tests,
without live IMAP or AI requests.
