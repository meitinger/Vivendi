Vivendi WebDAV File Access
==========================

This web application allows you to turn Vivendi's _Dateiablage_ into ordinary
folders by using Windows' WebDAV redirector (or any other WebDAV client for
that matter).  
The application honors the permissions set in Vivendi. It does not, however,
contain a different folder view for each _Bereich_ but rather shows them
altogether. To be clear: Files stored in different _Bereiche_ are of course
separated in different folders, each with different permissions, but if a file
is set to be **viewed** only in one _Bereich_ it will be displayed as long as
the user can access this _Bereich_. So in a way it's equivalent to open a
folder in Vivendi and going through each _Bereich_ in the top selector.

In general the application tracks who uploaded a file via WebDAV (this user is
called the _owner_) and does not allow the modification of files that where
stored directly in Vivendi. This behavior can the fine-tuned in `web.config`
(see the [configuration section](#configuration) below)  
The logon is done by using Windows Authentication, and the user name used in
Vivendi is the same as the Windows user name (minus the domain name). This of
course could easily be changed. It would even be possible to use basic
authentication and perform a logon with Vivendi directly. However, when using
the WebDAV redirector some limitation might apply on what is allowed without
SSL, see [Using the WebDAV Redirector](https://docs.microsoft.com/en-us/iis/publish/using-webdav/using-the-webdav-redirector#webdav-redirector-registry-settings).

As Vivendi allows multiple files to have the same name, file names are not
always translated 1:1 from Vivendi to WebDAV. If a user owns more than one or
no file at all with the same name in the same folder, the localization feature
of Windows Explorer is utilized to display the same name multiple times.
In that case the actually file name is an ID referencing the actual file.  
The same applies to files that have illegal characters in their name or adhere
to the same syntax as ID'ed file names.


Setup and Requirements
----------------------

### Compilation

The application makes heavy use of newer C# features and therefore requires
Roslyn to build. Roslyn can be enabled by installing its CodeDOM providers
with NuGet by running the following packet manager command:

    PM> Install-Package Microsoft.CodeDom.Providers.DotNetCompilerPlatform

This will install Roslyn in `Bin\roslyn`, the CodeDOM DLL in `Bin` and adjust
the compilation settings in `web.config` (which are already included in
`web.sample.config`).

### Databases

The web application needs read access to certain tables in the `VivAmbulant`
database (internally called _Data_), and read/write access to tables in
`VivDateiAblage` (internally called _Store_).  
To create the necessary permissions, first set up a database user named
`WebDAV`, then run the scripts contained in `data.sql` and `store.sql` against
the `VivAmbulant` and `VivDateiAblage` databases. Finally set the database
server name and `WebDAV` user password in the `connectionStrings` section of
`web.sample.config` and rename it to `web.config`.

### Configuration

The last step is to adjust the `webDAVSettings` section in `web.config`. Here
you can set which users or groups are allowed to modify resources that are
owned by someone else (`allowModificationOfOwnedResources` subsection) or even
resources that were not uploaded via WebDAV but created in Vivendi instead
(`allowModificationOfVivendiResources` subsection).  
Each of these sections allows to add users and groups in the form

    <add name="[<DOMAIN>\]<account>" type="User|Group" />

The `allowModificationOfOwnedResources` section also has two special boolean
attributes:

- `managers`: Allows all managers of an owner to modify his or her files.
- `team`:     Allows everyone who has the same manager as the owner to modify
              his or her files.

These attributes require domain functionality. In _very_ large domains the
`managers` attribute should be used with care.
