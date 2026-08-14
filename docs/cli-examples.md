# Public Devart CLI examples

Collected 2026-08-13 from Devart documentation, blogs, forums, and CI walkthroughs. These are the command lines a drop-in parser MUST tokenize. They are **not** this estate’s production jobs.

Secrets in originals were already placeholders (`sa`, `yourpassword`, `$(yourpassword)`). Keep them that way.

## 1. Schema — live databases, connection string, apply

Source: [Studio schema CLI](https://docs.devart.com/studio-for-sql-server/database-compare-and-sync/schema-compare/command-line-automation/command-line-interface.html)

```text
dbforgesql /schemacompare /source connection:"Data Source=demo-mssql\SQLEXPRESS02;Encrypt=False;Initial Catalog=AdventureWorks2022Dev;Integrated Security=False;User ID=JordanS" /target connection:"Data Source=demo-mssql\SQLEXPRESS01;Encrypt=False;Initial Catalog=AdventureWorks2022Test;Integrated Security=False;User ID=JordanS" /MappingIgnoreSpaces:Yes /MappingIgnoreCase:Yes /sync
```

## 2. Schema — split server/database/user/password, script only

Source: [Automate schema comparison](https://docs.devart.com/studio-for-sql-server/comparing-synchronizing-schemas/comparing-and-sync-schemas-cmd.html)

```text
dbforgesql /schemacompare /source server:SqlServer1 user:sa password:sa database:db1 /target server:SqlServer2 user:sa password:sa database:db2 /sync:"D:\compare_result.sql"
```

## 3. Schema — apply + log (no script file)

Source: [Schema options / usage examples](https://docs.devart.com/schema-compare-for-sql-server/using-the-command-line/options-used-in-the-command-line.html)

```text
schemacompare /schemacompare /source server:SqlServer1 user:sa password:sa database:db1 /target server:SqlServer2 user:sa password:sa database:db2 /sync /log:"D:\sync.log"
```

## 4. Schema — argfile

```text
dbforgesql /schemacompare /argfile:"D:\FileWithArguments.txt"
dbforgesql /schemacompare /argfile:"D:\FileWithArguments.txt" /sync
```

## 5. Schema — .scomp project, options override, HTML report, apply

```text
dbforgesql /schemacompare /compfile:"file_name.scomp" /icase:yes /IgnoreForeignKeys:yes /report:"report.html" /reportformat:HTML /groupby:objecttype /incsettings:T /sync
```

Forum variant with report + script ([Devart forums](https://forums.devart.com/viewtopic.php?t=32493)):

```text
schemacompare.com /schemacompare /source server:SqlServer1 user:sa password:sa database:db1 /target server:SqlServer2 user:sa password:sa database:db2 /sync:"D:\compare_result.sql" /report:"report.html" /reportformat:HTML /groupby:objecttype
```

## 6. Schema — .scomp compare only + log

```text
dbforgesql /schemacompare /compfile:"file_name.scomp" /log:"D:\log_file.log"
```

Forum SQL Agent job that **omitted a slash** and failed (`/sync log:` instead of `/sync /log:`). Parser MUST treat `log:` after `/sync` as illegal, not as `/log`:

```text
schemacompare.com /schemacompare /compfile:"C:\Comparisons\operations.scomp" /sync log:"C:\Comparisons\Logs\operations.log"
```

Expected: exit 10.

## 7. Schema — ignore permissions (typical prod)

Source: [Use objects filter](https://docs.devart.com/schema-compare-for-sql-server/reviewing-the-comparison-results/using-object-filter.html)

```text
dbforgesql.com /schemacompare /source connection:"Data Source=demo-mssql\SQLEXPRESS02;Initial Catalog=BicycleStoreDev;Integrated Security=False;User ID=sa" /target connection:"Data Source=demo-mssql\SQLEXPRESS02;Initial Catalog=BicycleStoreProd;Integrated Security=False;User ID=sa" /IgnorePermissions:Yes /IgnoreUserPermissions:Yes /sync
```

## 8. Schema — .scflt filter

```text
dbforgesql.com /schemacompare /source connection:"Data Source=demo-mssql\SQLEXPRESS02;Initial Catalog=BicycleStoreDev;Integrated Security=False;User ID=sa" /target connection:"Data Source=demo-mssql\SQLEXPRESS02;Initial Catalog=BicycleStoreProd;Integrated Security=False;User ID=sa" /IgnorePermissions:Yes /IgnoreUserPermissions:Yes /filter:"C:\jordansanders\Custom.scflt" /sync

dbforgesql.com /schemacompare /compfile:"C:\jordansanders\Comparison.scomp" /filter:"C:\jordansanders\Custom.scflt" /sync

dbforgesql.com /schemacompare /compfile:"D:\SchemaComparison.scomp" /filter:"D:\CustomFilter.scflt" /sync:"D:\BikeStoresSyncScript.sql"

schemacompare.com /schemacompare /compfile:"D:\path-to-your-scomp-file\comparison-file.scomp" /filter:"D:\path-to-your-scflt-file\object-filter.scflt" /sync
```

SQL Tools Professional path ([ignore permissions](https://docs.devart.com/schema-compare-for-sql-server/working-with-particular-cases/ignore-any-user-differences-and-object-permissions.html)):

```text
"C:\Program Files\Devart\dbForge SQL Tools Professional\dbForge Schema Compare for SQL Server\schemacompare.com" /schemacompare /compfile:"D:\path-to-your-scomp-file\comparison-file.scomp" /sync
```

## 9. Schema — Jenkins / compare-only for report

Source: [Jenkins sync step](https://docs.devart.com/devops-automation-for-sql-server/walkthrough/sync-step-jenkins.html) (omit `/sync` to compare only)

```text
"C:\Program Files\Devart\dbForge SQL Tools Professional\dbForge Schema Compare for SQL Server\schemacompare.com" /schemacompare /source connection:"Data Source=DESKTOP-9NIVJ84\SQLSERVER2022;Initial Catalog=A1;Integrated Security=True;User ID=DESKTOP-9NIVJ84\1" /target connection:"Data Source=DESKTOP-9NIVJ84\SQLSERVER2022;Initial Catalog=A2;Integrated Security=True;User ID=DESKTOP-9NIVJ84\1"
```

Jenkins blog ([automated deployment](https://www.devart.com/blog/automated-database-deployment-and-releases-with-jenkins-and-dbforge.html)):

```text
cd "C:\Program Files\Devart\dbForge Studio for SQL server"
dbforgesql.com /execute /connection:"%user connection%" /inputfile "D:\Temp\DevOps\Create_AdventureWorks2019.sql"
dbforgesql.com /schemacompare /compfile:"D:\Temp\DevOps\AdventureWorks2019.scomp" /sync

cd "C:\Program Files\Devart\dbForge Schema Compare for SQL Server"
schemacompare.com /execute /connection:"%user connection%" /inputfile "D:\Temp\DevOps\Create_AdventureWorks2019.sql"
schemacompare.com /schemacompare /compfile:"D:\Temp\DevOps\AdventureWorks2019.scomp" /sync
```

`/execute` is out of compare scope; the `/schemacompare /compfile /sync` lines are in scope.

## 10. Schema — generate scripts (NOT compare)

Source: [Devart forums](https://forums.devart.com/viewtopic.php?t=32821)

```text
schemacompare.com /script /connection:"Data Source=DBMSSQL\MSSQL2012;Integrated Security=False;User ID=sa" /projectfile:"D:\Scripts\AdventureWorks.backup"
```

v1: exit 10 (unsupported operation).

## 11. Data — .dcomp compare / script / log / report ladder

Source: [Automate data comparison](https://docs.devart.com/data-compare-for-sql-server/using-the-command-line/comparing-and-synchronizing-data.html)

```text
datacompare.com /datacompare /compfile:"D:\mycompdoc.dcomp"
datacompare.com /datacompare /compfile:"D:\mycompdoc.dcomp" /sync:"D:\mysync.sql"
datacompare.com /datacompare /compfile:"D:\mycompdoc.dcomp" /sync:"D:\mysync.sql" /log:"D:\mylog.log"
datacompare.com /datacompare /compfile:"D:\mycompdoc.dcomp" /sync:"D:\mysync.sql" /log:"D:\mylog.log" /report:"D:\myreport.html"
datacompare.com /datacompare /compfile:"D:\mycompdoc.dcomp" /sync:"D:\mysync.sql" /log:"D:\mylog.log" /report:"D:\myreport.html" /reportformat:HTML
```

Argfile (note unquoted path in the official example):

```text
datacompare.com /argfile:file_name.txt
```

## 12. Data — timestamped .bat

```bat
set TimeStamp=%date:~6,4%-%date:~3,2%-%date:~0,2%_%time:~0,2%-%time:~3,2%-%time:~6,2%

"C:\Program Files\Devart\Compare Bundle for SQL Server\dbForge Data Compare for SQL Server\datacompare.com" /datacompare /source connection:"Data Source=DBFSQLSRV\SQL2022;Initial Catalog=AdventureWorks2022;Integrated Security=False;User ID=yourusername" /target connection:"Data Source=demo-mssql\SQLEXPRESS02;Initial Catalog=AdventureWorks2022Test;Integrated Security=False;User ID=yourusername" /log:"D:\Log_File_%TimeStamp%.log" /sync:"Update_Script_%TimeStamp%.sql"
```

## 13. Data — Studio connection strings, apply + log, integrated security script

Source: [Studio data compare cmd](https://docs.devart.com/studio-for-sql-server/comparing-synchronizing-data/comparing-and-synchronizing-data-cmd.html)

```text
dbforgesql.com /datacompare /source connection:"Connect Timeout=120;Data Source=<source_server>;Initial Catalog=<source_database>;Integrated Security=False;User ID=<source_username>;Password=<source_pw>;Pooling=False" /target connection:"Connect Timeout=120;Data Source=<target_server>;Initial Catalog=<target_database>;Integrated Security=False;User ID=<target_username>;Password=<target_pw>;Pooling=False" /sync /log:"D:\sync.log"

dbforgesql.com /datacompare /source connection:"Data Source=<source_server>;Initial Catalog=<source_database>;Integrated Security=True" /target connection:"Data Source=<target_server>;Initial Catalog=<target_database>;Integrated Security=True" /sync:"D:\sync_script.sql"

dbforgesql.com /datacompare /source connection:"Data Source=demo-mssql\SQLEXPRESS02;Encrypt=False;Initial Catalog=AdventureWorks2022Dev;Integrated Security=False;User ID=JordanS" /target connection:"Data Source=demo-mssql\SQLEXPRESS01;Encrypt=False;Initial Catalog=AdventureWorks2022Test;Integrated Security=False;User ID=JordanS" /MappingIgnoreCase:Yes /MappingIgnoreUnderscores:Yes /sync:"C:\Users\JordanS\Desktop\AdventureWorks2022 (development) vs. AdventureWorks2022 (production).sql"
```

## 14. Data — split params, HTML report, disable DML, no comments

```text
datacompare /datacompare /source server:SqlServer1 user:sa password:pswd database:db1 /target server:SqlServer2 user:sa password:pswd database:db2 /sync /log:"D:\sync.log"

datacompare /datacompare /source server:SqlServer1 database:db1 user:sa password:pswd /target server:SqlServer2 database:db2 user:sa password:pswd /sync:"D:\compare_result.sql"

datacompare /datacompare /compfile:"SC1vsSC2.dcomp" /icase:yes /report:"report.html" /reportformat:HTML /sync

dbforgesql.com /datacompare /compfile:"D:\file.dcomp" /nocomments:yes /nodml:yes /report:"D:\report.html" /reportformat:HTML /sync
```

## 15. Data — backup as source (v1: exit 10)

```text
dbforgesql.com /datacompare /source backup:"D:\backup_file.bak" /target server:<target_server> database:<target_database> user:<user_name> password: /CheckIdentical:Yes /sync:"D:\sync_script.sql" /report:"D:\report_file.html"
```

## 16. Data — LOB via fileshare (v1: exit 10)

```text
dbforgesql.com /datacompare /compfile:"D:\workDir\DC1vsDC2.dcomp" /fspath:"\\SqlHost\Temp" /sync
```

## 17. Data — Azure DevOps pipeline

Source: [Integrate Data Compare in DevOps](https://docs.devart.com/data-compare-for-sql-server/using-the-command-line/integrate-data-compare-in-devops.html)

The pipeline expands `$(…)` **before** the process starts. ArtSync sees the expanded connection string.

```text
"C:\Program Files\Devart\dbForge SQL Tools Professional\dbForge Data Compare for SQL Server\datacompare.com" /datacompare /source connection:"Data Source=$(JSourceServer);Initial Catalog=$(JSourceDB);Integrated Security=False;User ID=$(yourusername);Password=$(yourpassword)" /target connection:"Data Source=$(JTargetServer);Initial Catalog=$(JTargetDB);Integrated Security=False;User ID=$(yourusername);Password=$(yourpassword)" /sync
```

## 18. Data — PowerShell scheduled job (compare then sync)

Source: [How to automatically synchronize data](https://www.devart.com/blog/how-to-automatically-synchronize-data-in-two-sql-server-databases-on-a-schedule.html)

```text
datacompare.com /datacompare /compfile:"D:\DataSync\Project\Database1vsDatabase2.dcomp" /log:"D:\DataSync\Outputs\DataOutput_STAMP.txt"
datacompare.com /datacompare /compfile:"D:\DataSync\Project\Database1vsDatabase2.dcomp" /log:"D:\DataSync\Outputs\DataOutput_STAMP.txt" /sync
```

Wrapper logic to emulate:

- First command: `100` → stop; `101` → run second; else → error.
- Second command (`/sync`): `0` → success; `100` → nothing to do.

A blog comment mentions `/rece` → exit `102`. It is not in the published script or current exit-code docs. Ignore unless a live job uses it.

## 19. Help

```text
dbforgesql.com /schemacompare /?
dbforgesql.com /datacompare /?
datacompare.com /datacompare /exitcodes
schemacompare.com /backup /?
```

## What did not show up

Searches of GitHub and Stack Overflow did **not** yield a public SQL Server estate’s real `dbforgesql.com` job. The closest third-party snippet is MySQL (`dbforgemysql.com /schemacompare … /filter:… /sync:file /log:file` from [SO 78082443](https://stackoverflow.com/questions/78082443/character-encoding-problem-when-importing-sql-with-dbforge-and-powershell-into-m)), which confirms the same switch grammar across products but is not a SQL Server acceptance test.

Azure SQL connections in public CLI samples use ordinary `Data Source=` strings (or `.scomp` saved from the GUI with Entra auth). There is no documented `server:*.database.windows.net` sample distinct from on-prem besides that.
