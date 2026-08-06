using JustyBase.Common.Models;
using System.Text.Json.Serialization;

namespace JustyBase.Common;

public sealed class AppOptions
{
    private const int ExtremeResultThreshold = 1_000_000;

    public List<string> StartsFolderPaths { get; set; } = [];
    public int ResultRowsLimit { get; set; } = ExtremeResultThreshold;
    public int? ResultRowsLimitWarning { get; set; }//currently only in pro
    /// <summary>When result rows exceed this threshold, spill to local SQLite and page the grid (MVP).</summary>
    public int ResultSpillThreshold { get; set; } = ExtremeResultThreshold;
    public int ResultSpillPageSize { get; set; } = ExtremeResultThreshold;
    public int ConnectionTimeout { get; set; } = 5; //s
    public int CommandTimeout { get; set; } = 3_600; //s = 3_600 = 1 hour
    //AllSnippets[name] = (snippetType, desc, text, keyword - not used)
    public Dictionary<string, (string snippetType, string? Description, string? Text, string? Keyword)> AllSnippets { get; set; } = [];
    public string SepInExportedCsv { get; set; } = ";";
    public string SepRowsInExportedCsv { get; set; } = "windows";
    public string EncondingName { get; set; } = "UTF-8";
    public string DecimalDelimInCsv { get; set; } = "'";
    public bool ImportExisting { get; set; }
    public bool DontRefreshImportedTableSchema { get; set; }
    public bool UseXlsb { get; set; } = true;
    public string DefaultXlsxSheetName { get; set; } = "sheet";
    public bool? CloseUndocked { get; set; } = true;
    public bool CollapseFoldingOnStartup { get; set; } = true;
    /// <summary>When true, errors/crashes are appended to local <c>errors.log</c>. Default off.</summary>
    public bool EnableFileLogging { get; set; } = false;
    public int ThemeNum { get; set; } = 0;
    public string? ConnectionNameInSchemaSearch { get; set; }
    public bool CaseSensitive { get; set; }
    public bool SearchInSource { get; set; }
    public bool WholeWords { get; set; }
    public bool RegexMode { get; set; }
    public bool RefreshOnStartupInSchemaSearch { get; set; }
    public bool SingleLineTabs { get; set; }
    public bool AutocompleteOnReturn { get; set; } = false;
    public bool ConfirmDocumentClosing { get; set; } = false;
    public double ControlContentThemeFontSize { get; set; } = 12.0;
    public double CompletitionFontSize { get; set; } = 13.0;
    public int DefaultFontSizeForDocuments { get; set; } = 13;
    public string DocumentFontName { get; set; } = "Cascadia Code";
    public double LineSpacing { get; set; } = 1.0;
    public bool ShowDetailsButton { get; set; } = true;
    public bool DoGcCollect { get; set; } = false;//remove ?
    public int LayoutNum { get; set; } = 1;
    public bool UseSplashScreen { get; set; } = true;
    public bool AutoDownloadUpdate { get; set; } = false;
    public bool AutoDownloadPlugins { get; set; } = true;
    public bool AllowToLoadPlugins { get; set; } = true;
    public bool UpdateMitigateNextGenFirewalls { get; set; } = false; // palo alto
    public int LimitHistoryMonths { get; set; } = 6;

    /// <summary>Master switch for SQL linter analysis.</summary>
    public bool SqlLinterEnabled { get; set; } = true;
    /// <summary>Severity for NZ001 (SELECT *): Off, Warning, or Error.</summary>
    public string LintSeverityNz001 { get; set; } = "Warning";
    /// <summary>Severity for NZ002/SQL043 (UPDATE without WHERE).</summary>
    public string LintSeverityNz002 { get; set; } = "Warning";
    /// <summary>Severity for NZ003/SQL044 (DELETE without WHERE).</summary>
    public string LintSeverityNz003 { get; set; } = "Warning";
    /// <summary>Severity for NZ011/SQL045 (CTAS missing DISTRIBUTE).</summary>
    public string LintSeverityNz011 { get; set; } = "Warning";
    /// <summary>Severity for NZ004 (CROSS JOIN).</summary>
    public string LintSeverityNz004 { get; set; } = "Warning";
    /// <summary>Severity for NZ005 (LIKE with leading wildcard).</summary>
    public string LintSeverityNz005 { get; set; } = "Warning";
    /// <summary>Severity for NZ008 (TRUNCATE).</summary>
    public string LintSeverityNz008 { get; set; } = "Warning";
    /// <summary>Severity for NZ012/SQL046 (UPDATE … AS alias).</summary>
    public string LintSeverityNz012 { get; set; } = "Warning";
    /// <summary>Severity for NZ013 (prefer UNION ALL).</summary>
    public string LintSeverityNz013 { get; set; } = "Warning";
    /// <summary>Severity for NZ015 (function in WHERE).</summary>
    public string LintSeverityNz015 { get; set; } = "Warning";
    /// <summary>Severity for NZ102 (JOIN without ON).</summary>
    public string LintSeverityNz102 { get; set; } = "Warning";

    /// <summary>
    /// When true, SQL editors request Fill-in-the-Middle ghost-text via the bundled llama.cpp
    /// llama-server subprocess hosting a FIM GGUF model. Default off.
    /// </summary>
    public bool EnableFimServer { get; set; }

    /// <summary>
    /// Selected embedded FIM model id (see JustyBase.Ai.Embedded FimModelIds).
    /// Default: qwen2.5-coder-3b (Medium preset).
    /// </summary>
    public string FimModelId { get; set; } = "qwen2.5-coder-3b";

    /// <summary>
    /// Idle delay after typing/caret stop before requesting an embedded FIM suggestion.
    /// Allowed: 250, 400, 600, 1000, 2000, 3000. Default 600.
    /// </summary>
    public int FimDebounceMs { get; set; } = 600;

    /// <summary>
    /// Max tokens for a single FIM ghost-text suggestion (20–200, default 50).
    /// </summary>
    public int FimMaxTokens { get; set; } = 50;

    /// <summary>Total prompt budget in tokens (prefix+suffix), mapped to chars via ~4 chars/token.</summary>
    public int FimMaxPromptTokens { get; set; } = 1536;

    /// <summary>Share of prompt budget used before the caret (0–1).</summary>
    public double FimPrefixPercentage { get; set; } = 0.65;

    /// <summary>Share of prompt budget used after the caret (0–1).</summary>
    public double FimSuffixPercentage { get; set; } = 0.35;

    /// <summary>
    /// When true, FIM prompts are prefixed with a compact comment block listing the
    /// columns/types of tables referenced by the statement near the caret (resolved from
    /// the in-memory schema snapshot — no database round-trip). Default off.
    /// </summary>
    public bool FimSchemaContext { get; set; }

    /// <summary>
    /// Max prompt tokens reserved for the FIM schema-context comment block (64–1024, default 256).
    /// The block is charged against the prefix budget; the code window keeps at least 25%.
    /// </summary>
    public int FimSchemaContextMaxTokens { get; set; } = 256;

    /// <summary>Named preset: Small / Medium / Large / Custom. Individual knobs may diverge → Custom.</summary>
    public string FimPreset { get; set; } = "Medium";

    /// <summary>
    /// llama.cpp gpu_layers offloaded for the FIM server (0 = CPU compute, 99 = as many as fit).
    /// Used when LlamaServerPreferVulkan is true. Default 99.
    /// </summary>
    public int FimGpuLayers { get; set; } = 99;

    /// <summary>Context size (tokens) for the FIM llama-server. Default 4096.</summary>
    public int FimCtxSize { get; set; } = 4096;

    /// <summary>Model ids for which the user accepted a required third-party license (Codestral, Gemma, …).</summary>
    public List<string> FimAcceptedLicenseModelIds { get; set; } = [];

    /// <summary>
    /// When true, AI Chat offers the "Embedded (local)" backend: a bundled llama.cpp llama-server
    /// hosting the selected chat GGUF model. Model is downloaded in Settings first. Default off.
    /// </summary>
    public bool EnableEmbeddedChatAi { get; set; }

    /// <summary>
    /// Selected embedded chat model id (see JustyBase.Ai.Embedded EmbeddedChatModelIds).
    /// Default: qwen3.5-4b (smallest, iGPU friendly).
    /// </summary>
    public string EmbeddedChatModelId { get; set; } = "qwen3.5-4b";

    /// <summary>
    /// llama.cpp gpu_layers offloaded for the embedded chat server (0 = CPU, 99 = as many as fit).
    /// Used when LlamaServerPreferVulkan is true. Default 99.
    /// </summary>
    public int EmbeddedChatGpuLayers { get; set; } = 99;

    /// <summary>Context size (tokens) for the embedded chat llama-server. Default 4096.</summary>
    public int EmbeddedChatCtxSize { get; set; } = 4096;

    /// <summary>Embedded chat model ids for which the user accepted a required third-party license.</summary>
    public List<string> EmbeddedChatAcceptedLicenseModelIds { get; set; } = [];

    /// <summary>
    /// Prefer the Vulkan variant of the bundled llama-server binary (AMD/Intel iGPU, etc.).
    /// Off selects the CPU (avx2) build. Default true.
    /// </summary>
    public bool LlamaServerPreferVulkan { get; set; } = true;

    // AI Chat sessions history
    public List<ChatSession> ChatSessions { get; set; } = [];

    /// <summary>
    /// Master switch for AI Chat. Default off — opt-in posture mirroring <see cref="EnableEmbeddedFimAi"/>.
    /// When false, the AI Chat dock panel is not created, "Fix in AI Chat" entry points are hidden /
    /// blocked, and AiChatViewModel short-circuits send.
    /// </summary>
    public bool EnableAiChat { get; set; }

    /// <summary>
    /// Default backend id: "codex", "openai-compatible" or "embedded".
    /// Legacy "ollama" / "lmstudio" values are migrated to "openai-compatible".
    /// </summary>
    public string? AiChatBackendId { get; set; } = "codex";

    /// <summary>Base URL of the OpenAI-compatible backend (LM Studio, Ollama /v1, llama.cpp, vLLM, …).</summary>
    public string AiChatOpenAiCompatibleEndpoint { get; set; } = "http://localhost:1234/v1";

    /// <summary>Optional bearer API key for the OpenAI-compatible backend (empty for local servers).</summary>
    public string? AiChatOpenAiCompatibleApiKey { get; set; }

    /// <summary>
    /// Default chat model id. The value is also updated when the model is selected in the AI Chat toolbar.
    /// </summary>
    public string AiChatDefaultModel { get; set; } = "gpt-5.6-luna";

    /// <summary>
    /// Default Codex reasoning effort and the last selected effort in AI Chat.
    /// </summary>
    public string AiChatDefaultReasoningEffort { get; set; } = "low";

    /// <summary>
    /// Default ChatMode slug on new sessions: "expert" / "sqlfix" / "simple".
    /// Resolved via <see cref="ChatModeExtensions.FromSlug"/>.
    /// </summary>
    public string AiChatDefaultMode { get; set; } = "expert";

    /// <summary>
    /// Auto-connect to the configured backend when the AI Chat panel is first shown (rather than the
    /// default lazy connect on first message send). Default off — preserves current behavior.
    /// </summary>
    public bool AiChatAutoConnect { get; set; }

    /// <summary>
    /// Retain at most N past sessions in <see cref="ChatSessions"/>. Older entries are dropped.
    /// Replaces the previously hardcoded limit of 10 (AiChatViewModel session save).
    /// Default 10.
    /// </summary>
    public int AiChatHistoryLimit { get; set; } = 10;

    /// <summary>
    /// System-prompt preamble override (free text). Empty = use the RoleDefinition from the active
    /// ChatModeConfig. When set, this text is prepended to the model role definition.
    /// </summary>
    public string AiChatSystemPromptOverride { get; set; } = string.Empty;

    /// <summary>
    /// Sampling temperature (0.0–2.0). Lower = deterministic, higher = creative. Default 0.7.
    /// </summary>
    public double AiChatTemperature { get; set; } = 0.7;

    /// <summary>
    /// Max tokens generated in a single assistant response. Default 2048.
    /// </summary>
    public int AiChatMaxTokens { get; set; } = 2048;

    /// <summary>
    /// HTTP request timeout (ms) for chat completion calls to the local backend. Default 60000.
    /// </summary>
    public int AiChatRequestTimeoutMs { get; set; } = 60000;

    /// <summary>
    /// Max retries on transient backend errors (connection refused, HTTP 5xx). Default 1.
    /// </summary>
    public int AiChatMaxRetries { get; set; } = 1;

    /// <summary>
    /// Named preset: "balanced" / "precise" / "creative" / "custom".
    /// Individual knobs (temperature, max tokens) may diverge → flips to "custom".
    /// </summary>
    public string AiChatPreset { get; set; } = "balanced";

    /// <summary>
    /// True once the user manually edits temperature / max tokens / etc., locking
    /// <see cref="AiChatPreset"/> to "custom". Unlike <see cref="EmbeddedFimAutoPresetApplied"/>,
    /// there is no hardware-based auto suggestion for chat (chat runs on external backends);
    /// this flag only tracks preset-vs-custom state.
    /// </summary>
    public bool AiChatPresetIsCustom { get; set; }

    // TODO: Consider converting to enum for type safety (requires migration of existing serialized data)
    public const string FAST_SNIPET_TXT = "fast";
    public const string TYPO_SNIPET_TXT = "typo";
    public const string STANDARD_SNIPET_TXT = "standard";

    public bool ResetPlugins { get; set; } = false;

    public void AddDefaultValues()
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        if (AllSnippets.Count == 0)
        {
            var tmp = new Dictionary<string, (string snippetType, string? desc, string? text, string? keyword)>()
            {
                {"sx",(FAST_SNIPET_TXT,null,"select","sx") },
                {"SX",(FAST_SNIPET_TXT,null,"SELECT","SX") },
                {"sx*",(FAST_SNIPET_TXT,null,"select * from","sx*") },
                {"SX*",(FAST_SNIPET_TXT,null,"SELECT * FROM","SX*") },
                {"wx",(FAST_SNIPET_TXT,null,"where","wx") },
                {"WX",(FAST_SNIPET_TXT,null,"WHERE","WX") },
                {"fx",(FAST_SNIPET_TXT,null,"from","fx") },
                {"FX",(FAST_SNIPET_TXT,null,"FROM","FX") },
                {"gx",(FAST_SNIPET_TXT,null,"group by","gx") },
                {"GX",(FAST_SNIPET_TXT,null,"GROUP BY","GX") },
                {"hx",(FAST_SNIPET_TXT,null,"having","hx") },
                {"HX",(FAST_SNIPET_TXT,null,"HAVING","HX") },
                {"lx",(FAST_SNIPET_TXT,null,"limit","lx") },
                {"LX",(FAST_SNIPET_TXT,null,"LIMIT","LX") },
                {"ox",(FAST_SNIPET_TXT,null,"order by","ox") },
                {"OX",(FAST_SNIPET_TXT,null,"ORDER BY","OX*") },
                {"dx",(FAST_SNIPET_TXT,null, "drop table","dx") },
                {"DX",(FAST_SNIPET_TXT,null, "DROP TABLE","DX") },
                {"ix",(FAST_SNIPET_TXT,null,"insert into","ix") },
                {"IX",(FAST_SNIPET_TXT,null,"INSERT INTO","IX") },
                {"ux",(FAST_SNIPET_TXT,null,"update","ux") },
                {"UX",(FAST_SNIPET_TXT,null,"UPDATE","UX") },
                {"tx",(FAST_SNIPET_TXT,null,"truncate","tx") },
                {"TX",(FAST_SNIPET_TXT,null,"TRUNCATE","TX") },
                {"cx",(FAST_SNIPET_TXT,null,"create","cx") },
                {"CX",(FAST_SNIPET_TXT,null,"CREATE","CX") },

                {"DISTINCT",(TYPO_SNIPET_TXT,null, null, null) },
                {"GROUP",(TYPO_SNIPET_TXT,null, null, null) },
                {"ORDER",(TYPO_SNIPET_TXT,null, null, null) },
                {"PARTITION",(TYPO_SNIPET_TXT,null, null, null) },
                {"BETWEEN",(TYPO_SNIPET_TXT,null, null, null) },
                {"LIMIT",(TYPO_SNIPET_TXT,null, null, null) },
                {"FIRST_VALUE",(TYPO_SNIPET_TXT,null, null, null) },
                {"LAST_VALUE",(TYPO_SNIPET_TXT,null, null, null) },
                {"DENSE_RANK",(TYPO_SNIPET_TXT,null, null, null) },
                {"DROP",(TYPO_SNIPET_TXT,null, null, null) },
                {"CROSS",(TYPO_SNIPET_TXT,null, null, null) },
                {"JOIN",(TYPO_SNIPET_TXT,null, null, null) },
                {"LEFT",(TYPO_SNIPET_TXT,null, null, null) },
                //{"INTO",(TYPO_SNIPET_TXT,null, null, null) },
                {"DATE_PART",(TYPO_SNIPET_TXT,null, null, null) },
                {"DECODE",(TYPO_SNIPET_TXT,null, null, null) },
                {"NULLIF",(TYPO_SNIPET_TXT,null, null, null) },
                {"COALESCE",(TYPO_SNIPET_TXT,null, null, null) },

                 {"CASE WHEN ${Caret} THEN ; END",(STANDARD_SNIPET_TXT,null,"CASE WHEN ${Caret}\nTHEN\n;\nEND", null) },
                 {"SELECT * FROM ${name}  ;",(STANDARD_SNIPET_TXT,null,"SELECT\n*\nFROM ${name} \n;", null) },
                 {"CREATE TABLE ${name} AS  (  )  DISTRIBUTE ON RANDOM;",(STANDARD_SNIPET_TXT,null,"CREATE TABLE ${name} AS \n(\n\n) \nDISTRIBUTE ON RANDOM;", null) },
                 {"CREATE TABLE IF NOT EXISTS ${name} AS  (  ) DISTRIBUTE ON RANDOM;",(STANDARD_SNIPET_TXT,null,"CREATE TABLE IF NOT EXISTS ${name} AS \n(\n\n) \nDISTRIBUTE ON RANDOM;", null) },
                 {"DROP TABLE ${name} IF EXISTS;",(STANDARD_SNIPET_TXT,null,"DROP TABLE ${name} IF EXISTS;", null) },
                 {"DROP TABLE ${name};",(STANDARD_SNIPET_TXT,null,"DROP TABLE ${name};", null) },
                 {"GROOM TABLE ${name} VERSIONS;",(STANDARD_SNIPET_TXT,null,"GROOM TABLE ${name} VERSIONS;", null) },
                 {"GROOM TABLE ${name} RECLAIM BACKUPSET NONE;",(STANDARD_SNIPET_TXT,null,"GROOM TABLE ${name} RECLAIM BACKUPSET NONE;", null) },
                 {"@mysessions",(STANDARD_SNIPET_TXT,null,
                 """
                    SELECT * FROM _V_SESSION WHERE USERNAME = USER@",@"@@let __Let $snpt_date_id1=20201220|$snpt_date_id2=20201031@",@"@@for __LetFor $snpt_date_id|20200131|202000229|20200331|20200430|20200531@",@"@@activequeries select
                        q.qs_planid
                        , q.qs_sessionid
                        , q.qs_clientid
                        , s.dbname
                        , s.username
                        , q.qs_cliipaddr
                        , q.qs_sql
                        , q.qs_state
                        , q.qs_tsubmit
                        , q.qs_tstart
                        , case when q.qs_tstart = 'epoch' then '0' else abstime 'now' - q.qs_tstart end as ELAPSED_SECS, initcap(q.qs_pritxt) AS PRIORYTY
                        , TRIM(TO_CHAR(ROUND(case when qs_estcost >= 0 then qs_estcost else 18446744073709551616 + qs_estcost end / 1000.0,0),'999 999 999')) AS ESTIMATED_SECS
                        , q.qs_estdisk / 1024 AS ESTIMATED_DISK_MB
                            , TRIM(TO_CHAR(ROUND(q.qs_estmem / 1024.0, 0), '999 999 999')) AS ESTIMATED_MEMORY_MB
                                , q.qs_snippets AS SNIPPETS
                    , q.qs_cursnipt AS CURRENTSNIPET
                    , q.qs_resrows AS RESOULTROWS
                    , q.qs_resbytes AS RESOULTBYES
                    from
                        _v_qrystat q,
                        _v_session s
                    where q.qs_sessionid = s.id
                 """
                 , null) },

                {"@tableSizes",(STANDARD_SNIPET_TXT,null,
                 """
                 --SET CATALOG TEST;
                 SELECT OBJID, TABLENAME, OWNER, 
                     CREATEDATE, RELNATTS, ALLOCATED_BYTES::bigint as ALLOCATED_BYTES, USED_BYTES::bigint as USED_BYTES, SKEW, CAST(NULL as NUMERIC) as ROW_COUNT, 
                     ALLOCATED_BLOCKS, USED_BLOCKS, BLOCK_SIZE, USED_MIN, USED_MAX, USED_AVG, 
                     RELDISTMETHOD, MATER_COUNT, MATER_BLOCKS, MATER_BYTES, MATER_OVERHEAD
                 FROM _V_TABLE_STORAGE_STAT
                 WHERE UPPER(OBJTYPE) = 'TABLE'
                 ORDER BY  TABLENAME, OWNER;
            
                 select RELNAME, RELREFS, RELTUPLES from _T_CLASS;
            
                 SELECT o.OBJNAME as TABLENAME, o.OWNER, z.DSID, z.HWID, NULL as DATA_PART,
                     z.ALLOCATED_BLOCKS, z.USED_BLOCKS, z.ALLOCATED_BYTES::bigint as ALLOCATED_BYTES, z.USED_BYTES::bigint as USED_BYTES,
                             z.SORTED_BLOCKS, z.SORTED_BYTES
                     FROM _V_SYS_OBJECT_DSLICE_INFO z
                     join _v_object_data o on o.objid = z.tblid
                 where o.objdb = current_db;@",@"@@deleted SET show_deleted_records = 1;
                 select createxid,deletexid, * from YOURTABLENAME WHERE deletexid <> 0;
                 SET show_deleted_records = 0;
                 """
                 , null) },
                {"@merge",(STANDARD_SNIPET_TXT,null,
                 """
                 MERGE INTO merge_demo1 AS A 
                 using merge_demo2 AS B
                 ON A.ID = B.ID
                 WHEN MATCHED THEN
                 UPDATE SET A.LastName = B.LastName
                 WHEN NOT MATCHED THEN
                 INSERT VALUES (B.ID, B.FirstName, B.LastName); 
                 --https://dwgeek.com/netezza-merge-command-manipulate-records.html/
                 """
                 , null) },
                {"@window",(STANDARD_SNIPET_TXT,null,
                 """
                 https://www.ibm.com/support/knowledgecenter/SSTNZ3/com.ibm.ips.doc/postgresql/dbuser/c_dbuser_window_analytic_funcs.html@",@"@@window2 <partition_by_clause> = PARTITION BY <value_expression> [, ...]+
                 <order_by_clause> = ORDER BY <value_expression> [asc | desc] [nulls 
                 {first|last}] [, ...]+
                 <frame_spec_clause> = <frame_extent> [<exclusion clause>]
                 <frame_extent> = 
                     ROWS  UNBOUNDED PRECEDING
                     |ROWS  <constant> PRECEDING
                     |ROWS   CURRENT ROW
                     |RANGE  UNBOUNDED PRECEDING
                     |RANGE  <constant> PRECEDING
                     |RANGE  CURRENT ROW
                     |ROWS BETWEEN {UNBOUNDED PRECEDING| <constant> PRECEDING | CURRENT 
                 ROW } AND { UNBOUNDED FOLLOWING | <constant>  FOLLOWING | CURRENT ROW }
                     |RANGE BETWEEN {UNBOUNDED PRECEDING| <constant> PRECEDING | CURRENT 
                 ROW } AND { UNBOUNDED FOLLOWING | <constant>  FOLLOWING | CURRENT ROW } 
                 <exclusion_clause> =  EXCLUDE CURRENT ROW | EXCLUDE TIES | EXCLUDE 
                 GROUP | EXCLUDE  NO OTHERS
                 """
                 , null) },

                {"declare",(STANDARD_SNIPET_TXT,"declare variable","declare &${name} = ${value};${Caret}", "declare") },
                {"REGEXP_LIKE",(STANDARD_SNIPET_TXT,"REGEXP_LIKE","REGEXP_LIKE('${input}','${pattern}')${Caret}", "REGEXP_LIKE") },
                {"@export xlsb",(STANDARD_SNIPET_TXT,"export to excel file",$"@expXlsx: SELECT * FROM ${{tableName}} -> {desktopPath}\\${{fileName}}.xlsb${{Caret}};", "export") },
                {"@export csv",(STANDARD_SNIPET_TXT,"export to csv",$"@expCsv: SELECT * FROM ${{tableName}} -> {desktopPath}\\${{fileName}}.csv${{Caret}};", "export") },

                {"@export csv/parquet full", (STANDARD_SNIPET_TXT,"export to csv/parquet with options",
                    $$"""
                    expCsv: SELECT * FROM ${tableName} -> {{desktopPath}}\${fileName}.csv
                    {
                    #delimiter ${|}
                    #lineDelimiter ${windows}
                    #encoding ${UTF8}
                    #compression ${zstd}
                    #upFrontRowsCount true
                    }${Caret};
                    """
                    ,"export")},

                {"SELECT", (STANDARD_SNIPET_TXT,"SELECT","SELECT","SELECT") },
                {"AS", (STANDARD_SNIPET_TXT,"AS","AS","AS") },
                {"WHERE", (STANDARD_SNIPET_TXT,"WHERE","WHERE","WHERE") },
                {"HAVING", (STANDARD_SNIPET_TXT,"HAVING","HAVING","HAVING") },
                {"FROM", (STANDARD_SNIPET_TXT,"FROM","FROM","FROM") },
                {"GROUP BY", (STANDARD_SNIPET_TXT,"GROUP BY","GROUP BY","GROUP BY") },
                {"ORDER BY", (STANDARD_SNIPET_TXT,"ORDER BY","ORDER BY","ORDER BY") },
                {"ROW_NUMBER()", (STANDARD_SNIPET_TXT,"ROW_NUMBER()","ROW_NUMBER()","ROW_NUMBER()") },
                {"PARTITION BY", (STANDARD_SNIPET_TXT,"PARTITION BY","PARTITION BY","PARTITION BY") },
                {"ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW", (STANDARD_SNIPET_TXT,"ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW","ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW","ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW") },
                {"UNBOUNDED", (STANDARD_SNIPET_TXT,"UNBOUNDED","UNBOUNDED","UNBOUNDED") },
                {"FOLLOWING", (STANDARD_SNIPET_TXT,"FOLLOWING","FOLLOWING","FOLLOWING") },
                {"LIKE", (STANDARD_SNIPET_TXT,"LIKE","LIKE","LIKE") },
                {"UPDATE", (STANDARD_SNIPET_TXT,"UPDATE","UPDATE","UPDATE") },
                {"DISTRIBUTE", (STANDARD_SNIPET_TXT,"DISTRIBUTE","DISTRIBUTE","DISTRIBUTE") },
                {"RANDOM", (STANDARD_SNIPET_TXT,"RANDOM","RANDOM","RANDOM") },
                {"SUBSTRING", (STANDARD_SNIPET_TXT,"SUBSTRING","SUBSTRING","SUBSTRING") },
                {"UNION ALL", (STANDARD_SNIPET_TXT,"UNION ALL","UNION ALL","UNION ALL") },
                {"ALL ", (STANDARD_SNIPET_TXT,"ALL ","ALL ","ALL ") },
                {"COMMIT", (STANDARD_SNIPET_TXT,"COMMIT","COMMIT","COMMIT") },
                {"UNBOUNDED FOLLOWING", (STANDARD_SNIPET_TXT,"UNBOUNDED FOLLOWING","UNBOUNDED FOLLOWING","UNBOUNDED FOLLOWING") },
                {"UNBOUNDED PRECEDING", (STANDARD_SNIPET_TXT,"UNBOUNDED PRECEDING","UNBOUNDED PRECEDING","UNBOUNDED PRECEDING") },
                {"PRECEDING", (STANDARD_SNIPET_TXT,"PRECEDING","PRECEDING","PRECEDING") },
                {"STRLEFT", (STANDARD_SNIPET_TXT,"STRLEFT","STRLEFT","STRLEFT") },
                {"STRRIGHT", (STANDARD_SNIPET_TXT,"STRRIGHT","STRRIGHT","STRRIGHT") },
                {"RENAME", (STANDARD_SNIPET_TXT,"RENAME","RENAME","RENAME") },
            };

            foreach (var item in tmp)
            {
                AllSnippets.Add(item.Key, item.Value);
            }
        }

        ResultRowsLimitWarning ??= ResultRowsLimit / 10;

        if (ResultRowsLimit < ExtremeResultThreshold)
        {
            ResultRowsLimit = ExtremeResultThreshold;
        }

        if (ResultSpillThreshold < ExtremeResultThreshold)
        {
            ResultSpillThreshold = ExtremeResultThreshold;
        }

        if (ResultSpillPageSize < ExtremeResultThreshold)
        {
            ResultSpillPageSize = ExtremeResultThreshold;
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppOptions))]
[JsonSerializable(typeof(List<ChatSession>))]
[JsonSerializable(typeof(ChatSession))]
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(ChatAttachment))]
[JsonSerializable(typeof(List<ChatAttachment>))]
public partial class MyJsonContextAppOptions : JsonSerializerContext
{
}
