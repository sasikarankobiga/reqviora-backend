

\# REQVIORA - Rashmina Database Design



\## 1. Database Technology



\- Database: PostgreSQL

\- Cloud Database Platform: Supabase

\- ORM: Entity Framework Core 10

\- Database Provider: Npgsql

\- Backend: ASP.NET Core



\## 2. Assigned Database Entities



\### Workspace

Purpose: Stores workspace information.



Fields:

\- Id: Primary Key (Guid)

\- Name: Required, maximum 150 characters

\- OwnerUserId: Foreign Key to User



\### Project

Purpose: Stores projects belonging to workspaces.



Fields:

\- Id: Primary Key (Guid)

\- Name: Required, maximum 200 characters

\- Description: Maximum 4000 characters

\- OwnerId: Foreign Key to User

\- WorkspaceId: Foreign Key to Workspace



\### ProjectMember

Purpose: Stores users assigned to projects.



Fields:

\- Id: Primary Key (Guid)

\- ProjectId: Foreign Key to Project

\- UserId: Foreign Key to User

\- Role: Required, maximum 30 characters

\- JoinedAtUtc: Date and time joined



Unique Index:

\- ProjectId + UserId



\### ProjectDraft

Purpose: Stores the current editable project draft.



Fields:

\- Id: Primary Key (Guid)

\- ProjectId: Foreign Key to Project

\- DraftJson: Required

\- Version: Required

\- ConcurrencyToken: Required

\- UpdatedAtUtc: Required



Unique Index:

\- ProjectId



\## 3. Entity Relationship Diagram



```mermaid

erDiagram

&#x20;   User ||--o{ Workspace : owns

&#x20;   User ||--o{ Project : owns

&#x20;   Workspace ||--o{ Project : contains

&#x20;   User ||--o{ ProjectMember : joins

&#x20;   Project ||--o{ ProjectMember : has

&#x20;   Project ||--o| ProjectDraft : stores



&#x20;   User {

&#x20;       Guid Id PK

&#x20;       string NormalizedEmail

&#x20;   }



&#x20;   Workspace {

&#x20;       Guid Id PK

&#x20;       string Name

&#x20;       Guid OwnerUserId FK

&#x20;   }



&#x20;   Project {

&#x20;       Guid Id PK

&#x20;       string Name

&#x20;       Guid OwnerId FK

&#x20;       Guid WorkspaceId FK

&#x20;   }



&#x20;   ProjectMember {

&#x20;       Guid Id PK

&#x20;       Guid ProjectId FK

&#x20;       Guid UserId FK

&#x20;       string Role

&#x20;   }



&#x20;   ProjectDraft {

&#x20;       Guid Id PK

&#x20;       Guid ProjectId FK

&#x20;       string DraftJson

&#x20;       int Version

&#x20;       Guid ConcurrencyToken

&#x20;       DateTime UpdatedAtUtc

&#x20;   }

```



\## 4. Database Relationships



\- One User can own multiple Workspaces.

\- One Workspace can contain multiple Projects.

\- One User can own multiple Projects.

\- One Project can have multiple ProjectMembers.

\- One User can be a member of multiple Projects.

\- One Project can have at most one current ProjectDraft.



\## 5. Database Constraints



Workspace:

\- OwnerUserId references User.Id.

\- Delete behavior: Restrict.



Project:

\- OwnerId references User.Id.

\- WorkspaceId references Workspace.Id.

\- Delete behavior: Restrict.



ProjectMember:

\- ProjectId references Project.Id.

\- UserId references User.Id.

\- ProjectId + UserId must be unique.

\- Project deletion: Cascade.

\- User deletion: Restrict.



ProjectDraft:

\- ProjectId references Project.Id.

\- ProjectId must be unique.

\- Project deletion: Cascade.



\## 6. Draft Save and Resume Logic



SaveAsync:

\- Creates a new draft with Version 1.

\- Saves draft JSON.

\- Generates a concurrency token.

\- Requires the current token when updating a draft.

\- Increases version after a successful update.



ResumeAsync:

\- Finds a draft using ProjectId.

\- Returns the saved draft.

\- Returns null when a draft does not exist.



Concurrency:

\- Prevents updates using an outdated token.

\- Throws a concurrency exception when a stale token is supplied.



\## 7. Testing



Five test cases have been written:



1\. Resume a draft that does not exist.

2\. Save a new project draft.

3\. Resume an existing project draft.

4\. Update a draft with the correct token.

5\. Reject an update with a stale token.



Status:

\- Test project build: Successful.

\- Test execution: Blocked by Windows Application Control.

\- Tests use SQLite in-memory with foreign keys disabled.

\- PostgreSQL integration testing: Pending.



\## 8. EF Core Migrations



Existing migrations:

\- InitialCreate

\- AddAuthenticationSessions



New migration:

\- AddWorkspaceMembersAndProjectDraft: Pending



Reason:

Local Windows Application Control blocks the

Reqviora.Infrastructure.dll during EF migration execution.



The shared Supabase database must not be updated

without team approval.



\## 9. Database Setup Instructions



1\. Use the team-approved Supabase PostgreSQL database.

2\. Configure the backend PostgreSQL connection string.

3\. Store database credentials securely outside Git.

4\. Keep the existing EF Core migrations.

5\. Generate and review the new migration in an approved environment.

6\. Apply reviewed migrations after team coordination.

7\. Verify project draft Save/Resume against PostgreSQL.



\## 10. Git Status



\- Branch: Rashmina

\- Latest verified push: 214171f

\- Current instruction: Continue coding locally.

\- Do not push further changes until team approval.



