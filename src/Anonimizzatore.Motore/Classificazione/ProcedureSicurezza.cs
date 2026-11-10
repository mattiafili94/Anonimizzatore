namespace Anonimizzatore.Motore.Classificazione;

/// <summary>Che cosa contiene l'argomento di una procedura di sistema per la sicurezza.</summary>
internal enum ArgSicurezza
{
    Login,
    Utente,
    Ruolo,
    /// <summary>Un utente o un ruolo (es. il membro di <c>sp_addrolemember</c>).</summary>
    Principale,
    Database,
    ServerCollegato,
    /// <summary>Un server o un'istanza scritti come testo (<c>SRVSQL01\ISTANZA</c>): <c>@datasrc</c>, <c>@location</c>.</summary>
    Host,
    Password,
    Sid,
    /// <summary>Il nome di un job di SQL Agent.</summary>
    Job,
}

/// <summary>
/// Le procedure di sistema che creano o modificano login, utenti e ruoli ricevono i nomi (e le password!) come
/// stringhe. Per ognuna: il nome di ogni parametro, in ordine, e che cosa contiene (<c>null</c> = non interessa).
/// </summary>
internal static class ProcedureSicurezza
{
    private static readonly Dictionary<string, (string Nome, ArgSicurezza? Tipo)[]> Firme = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sp_addlogin"] = [("@loginame", ArgSicurezza.Login), ("@passwd", ArgSicurezza.Password), ("@defdb", ArgSicurezza.Database),
            ("@deflanguage", null), ("@sid", ArgSicurezza.Sid), ("@encryptopt", null)],
        ["sp_droplogin"] = [("@loginame", ArgSicurezza.Login)],
        ["sp_password"] = [("@old", ArgSicurezza.Password), ("@new", ArgSicurezza.Password), ("@loginame", ArgSicurezza.Login)],
        ["sp_defaultdb"] = [("@loginame", ArgSicurezza.Login), ("@defdb", ArgSicurezza.Database)],
        ["sp_defaultlanguage"] = [("@loginame", ArgSicurezza.Login)],
        ["sp_changedbowner"] = [("@loginame", ArgSicurezza.Login)],
        ["sp_addrolemember"] = [("@rolename", ArgSicurezza.Ruolo), ("@membername", ArgSicurezza.Principale)],
        ["sp_droprolemember"] = [("@rolename", ArgSicurezza.Ruolo), ("@membername", ArgSicurezza.Principale)],
        ["sp_addsrvrolemember"] = [("@loginame", ArgSicurezza.Login), ("@rolename", ArgSicurezza.Ruolo)],
        ["sp_dropsrvrolemember"] = [("@loginame", ArgSicurezza.Login), ("@rolename", ArgSicurezza.Ruolo)],
        ["sp_adduser"] = [("@loginame", ArgSicurezza.Login), ("@name_in_db", ArgSicurezza.Utente), ("@grpname", ArgSicurezza.Ruolo)],
        ["sp_grantdbaccess"] = [("@loginame", ArgSicurezza.Login), ("@name_in_db", ArgSicurezza.Utente)],
        ["sp_revokedbaccess"] = [("@name_in_db", ArgSicurezza.Utente)],
        ["sp_dropuser"] = [("@name_in_db", ArgSicurezza.Utente)],
        ["sp_addrole"] = [("@rolename", ArgSicurezza.Ruolo), ("@ownername", ArgSicurezza.Principale)],
        ["sp_droprole"] = [("@rolename", ArgSicurezza.Ruolo)],
        ["sp_change_users_login"] = [("@Action", null), ("@UserNamePattern", ArgSicurezza.Utente),
            ("@LoginName", ArgSicurezza.Login), ("@Password", ArgSicurezza.Password)],
        // @provstr (stringa di connessione) è lasciata all'analizzatore dell'infrastruttura, che la legge come testo.
        ["sp_addlinkedserver"] = [("@server", ArgSicurezza.ServerCollegato), ("@srvproduct", null), ("@provider", null),
            ("@datasrc", ArgSicurezza.Host), ("@location", ArgSicurezza.Host), ("@provstr", null), ("@catalog", ArgSicurezza.Database)],
        ["sp_addserver"] = [("@server", ArgSicurezza.ServerCollegato), ("@local", null), ("@duplicate_ok", null)],
        ["sp_dropserver"] = [("@server", ArgSicurezza.ServerCollegato)],
        ["sp_addlinkedsrvlogin"] = [("@rmtsrvname", ArgSicurezza.ServerCollegato), ("@useself", null),
            ("@locallogin", ArgSicurezza.Login), ("@rmtuser", ArgSicurezza.Login), ("@rmtpassword", ArgSicurezza.Password)],
        ["sp_droplinkedsrvlogin"] = [("@rmtsrvname", ArgSicurezza.ServerCollegato), ("@locallogin", ArgSicurezza.Login)],

        // Job di SQL Agent. Il @command di sp_add_jobstep è T-SQL (SQL dinamico) o una riga di comando: lo trattano le
        // stringhe; i nomi dei passi (@step_name) e le descrizioni sono testo libero.
        ["sp_add_job"] = [("@job_name", ArgSicurezza.Job), ("@enabled", null), ("@description", null), ("@start_step_id", null),
            ("@category_name", null), ("@category_id", null), ("@owner_login_name", ArgSicurezza.Login)],
        ["sp_update_job"] = [("@job_id", null), ("@job_name", ArgSicurezza.Job), ("@new_name", ArgSicurezza.Job), ("@enabled", null),
            ("@description", null), ("@start_step_id", null), ("@category_name", null), ("@owner_login_name", ArgSicurezza.Login)],
        ["sp_delete_job"] = [("@job_id", null), ("@job_name", ArgSicurezza.Job)],
        ["sp_start_job"] = [("@job_name", ArgSicurezza.Job), ("@job_id", null), ("@error_flag", null), ("@server_name", ArgSicurezza.Host)],
        ["sp_stop_job"] = [("@job_name", ArgSicurezza.Job)],
        ["sp_add_jobstep"] = [("@job_id", null), ("@job_name", ArgSicurezza.Job), ("@step_id", null), ("@step_name", null),
            ("@subsystem", null), ("@command", null), ("@additional_parameters", null), ("@cmdexec_success_code", null),
            ("@on_success_action", null), ("@on_success_step_id", null), ("@on_fail_action", null), ("@on_fail_step_id", null),
            ("@server", ArgSicurezza.Host), ("@database_name", ArgSicurezza.Database), ("@database_user_name", ArgSicurezza.Utente)],
        ["sp_update_jobstep"] = [("@job_id", null), ("@job_name", ArgSicurezza.Job), ("@step_id", null), ("@step_name", null),
            ("@subsystem", null), ("@command", null), ("@additional_parameters", null), ("@cmdexec_success_code", null),
            ("@on_success_action", null), ("@on_success_step_id", null), ("@on_fail_action", null), ("@on_fail_step_id", null),
            ("@server", ArgSicurezza.Host), ("@database_name", ArgSicurezza.Database), ("@database_user_name", ArgSicurezza.Utente)],
        ["sp_add_jobserver"] = [("@job_id", null), ("@job_name", ArgSicurezza.Job), ("@server_name", ArgSicurezza.Host)],
        ["sp_delete_jobserver"] = [("@job_id", null), ("@job_name", ArgSicurezza.Job), ("@server_name", ArgSicurezza.Host)],
        ["sp_add_jobschedule"] = [("@job_id", null), ("@job_name", ArgSicurezza.Job)],
        ["sp_attach_schedule"] = [("@job_id", null), ("@job_name", ArgSicurezza.Job)],
    };

    public static IReadOnlyList<(string Nome, ArgSicurezza? Tipo)>? Firma(string procedura) =>
        Firme.TryGetValue(procedura, out var firma) ? firma : null;
}
