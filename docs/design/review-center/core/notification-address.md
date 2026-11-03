### NotificationAddress

![Core Structure](CoreView.svg)

#### Purpose

`NotificationAddress` decides whether a text is exactly one plain e-mail address that may be used as the recipient of
a notification, and gives that address in one normal form. Its single responsibility is this check. It is the only
place where the rules for an address are written, so the page that takes the address, the service that mails to it,
and the message that carries it all agree. The rules are deliberately stricter than the mail standard: the server
mails only to a plain address, and everything that could change the header of a mail or aim the mail at someone else is
refused.

#### Data Model

The unit is a static class without state. It exposes the longest accepted address (`MaxLength`, 254 characters).
The longest local part (64 characters) and the longest domain label (63 characters) are private limits of the check.

An accepted address has these parts:

- a local part of one or more groups of letters, digits, and the characters ``!#$%&'*+/=?^_`{|}~-``, joined by single
  dots (no dot at the start or the end, and no two dots in a row);
- exactly one at sign;
- a domain of at least two labels joined by single dots, each label made of letters, digits, and hyphens, with no
  hyphen at the start or the end of a label.

Letters and digits are the ASCII ones only. Surrounding white space is ignored, and the result is the trimmed address
in lower case.

#### Key Methods

**TryNormalize**: Checks a text and gives the address in its normal form.

- *Parameters*: The text to check (it may be null) and the normalized address (output).
- *Returns*: True when the text is exactly one acceptable address; otherwise false.
- *Postconditions*: When the result is true, the output is the trimmed, lower case address, at most 254 characters
  long. When the result is false, the output is the empty string. A control character anywhere (a line break, a tab, a
  NUL), a character outside ASCII, a display name, a comment, a quoted local part, an address literal in brackets, a
  list of addresses, a domain with one label, an empty part, a misplaced dot or hyphen, a local part of more than
  64 characters, a label of more than 63 characters, and a total of more than 254 characters all give false. Nothing
  is thrown for any input.

**IsValid**: Tells whether a text is an acceptable address.

- *Parameters*: The text to check (it may be null).
- *Returns*: The same answer as `TryNormalize`, without the address.

#### Error Handling

The unit never throws and never reports why a text was refused: a refused text may be attacker controlled, and the
caller shows its own fixed message. No input is written to a log.

#### Dependencies

None. The unit uses only the .NET base class library.

#### Callers

- **NotificationPreferenceService** — checks the address a user wants to confirm.
- **NotificationMessage** — checks and normalizes the recipient when a message is built.
- **Account page** — limits the typed address to `MaxLength` characters.
- **Notification options** — check with `IsValid` the addresses that an administrator configures.
- **ReviewCenterDbContext** — sizes the address columns with `MaxLength`.
