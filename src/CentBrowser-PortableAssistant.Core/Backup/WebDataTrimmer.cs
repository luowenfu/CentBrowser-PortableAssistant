using Microsoft.Data.Sqlite;

namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>Web Data 里只留搜索引擎，清掉自动填充、支付和令牌的行。表结构留下，避免浏览器整库重建。</summary>
public static class WebDataTrimmer
{
    private static readonly string[] SensitiveTables =
    [
        "plus_addresses", "plus_address_sync_model_type_state", "plus_address_sync_entity_metadata",
        "token_service",
        "addresses", "address_type_tokens",
        "autofill", "autofill_sync_metadata", "autofill_model_type_state",
        "credit_cards", "local_ibans", "masked_credit_cards", "server_card_metadata",
        "payments_customer_data", "server_card_cloud_token_data",
        "offer_data", "offer_eligible_instrument", "offer_merchant_domain",
        "virtual_card_usage_data", "local_stored_cvc", "server_stored_cvc",
        "masked_bank_accounts", "masked_bank_accounts_metadata",
        "masked_ibans", "masked_ibans_metadata",
        "masked_credit_card_benefits", "benefit_merchant_domains",
        "generic_payment_instruments",
        "secure_payment_confirmation_instrument", "secure_payment_confirmation_browser_bound_key",
        "loyalty_cards", "loyalty_card_merchant_domain",
        "autofill_ai_attributes", "autofill_ai_entities", "autofill_ai_entities_metadata",
        "valuables_metadata", "payment_instrument_creation_options",
    ];

    public static void KeepSearchEngines(string dbPath)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false };
        using var conn = new SqliteConnection(builder.ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var list = conn.CreateCommand())
        {
            list.Transaction = tx;
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            using var reader = list.ExecuteReader();
            while (reader.Read()) existing.Add(reader.GetString(0));
        }

        foreach (var table in SensitiveTables)
        {
            if (!existing.Contains(table)) continue;
            using var delete = conn.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = $"DELETE FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            delete.ExecuteNonQuery();
        }
        tx.Commit();

        using var checkpoint = conn.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        checkpoint.ExecuteNonQuery();
    }
}
