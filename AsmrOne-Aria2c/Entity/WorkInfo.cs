using Newtonsoft.Json;

namespace AsmrOne_Aria2c.Entity;

public class WorkInfo
{
    public int id { get; set; }
    public string title { get; set; }
    public int circle_id { get; set; }
    public string name { get; set; }
    public bool nsfw { get; set; }
    public string release { get; set; }
    public int dl_count { get; set; }
    public int price { get; set; }
    public int review_count { get; set; }
    public int rate_count { get; set; }
    public double rate_average_2dp { get; set; }
    public List<RateCountDetail> rate_count_detail { get; set; }
    public List<Rank> rank { get; set; }
    public bool has_subtitle { get; set; }
    public string create_date { get; set; }
    public List<Va> vas { get; set; }
    public List<Tag> tags { get; set; }
    public List<LanguageEdition> language_editions { get; set; }
    public object original_workno { get; set; }
    public List<OtherLanguageEditionsInDb> other_language_editions_in_db { get; set; }
    public TranslationInfo translation_info { get; set; }
    public string work_attributes { get; set; }
    public string age_category_string { get; set; }
    public int duration { get; set; }
    public string source_type { get; set; }
    public string source_id { get; set; }
    public string source_url { get; set; }
    public Circle circle { get; set; }
    public string samCoverUrl { get; set; }
    public string thumbnailCoverUrl { get; set; }
    public string mainCoverUrl { get; set; }
}

public class Circle
{
    public int id { get; set; }
    public string name { get; set; }
    public string source_id { get; set; }
    public string source_type { get; set; }
}

public class EnUs
{
    public string name { get; set; }
    public List<object> history { get; set; }
}

public class History
{
    public string name { get; set; }
    public long deprecatedAt { get; set; }
}

public class I18n
{
    [JsonProperty("en-us")] public EnUs enus { get; set; }

    [JsonProperty("ja-jp")] public JaJp jajp { get; set; }

    [JsonProperty("zh-cn")] public ZhCn zhcn { get; set; }
}

public class JaJp
{
    public string name { get; set; }
}

public class LanguageEdition
{
    public string lang { get; set; }
    public string label { get; set; }
    public string workno { get; set; }
    public int edition_id { get; set; }
    public string edition_type { get; set; }
    public int display_order { get; set; }
}

public class OtherLanguageEditionsInDb
{
    public int id { get; set; }
    public string lang { get; set; }
    public string title { get; set; }
    public string source_id { get; set; }
    public bool is_original { get; set; }
    public string source_type { get; set; }
}

public class Rank
{
    public string term { get; set; }
    public string category { get; set; }
    public int rank { get; set; }
    public string rank_date { get; set; }
}

public class RateCountDetail
{
    public int review_point { get; set; }
    public int count { get; set; }
    public int ratio { get; set; }
}

public class Tag
{
    public int id { get; set; }
    public I18n i18n { get; set; }
    public string name { get; set; }
    public object created_by { get; set; }
    public int upvote { get; set; }
    public int downvote { get; set; }
}

public class TranslationInfo
{
    public object lang { get; set; }
    public bool is_child { get; set; }
    public bool is_parent { get; set; }
    public bool is_original { get; set; }
    public bool is_volunteer { get; set; }
    public object child_worknos { get; set; }
    public object parent_workno { get; set; }
    public object original_workno { get; set; }
    public bool is_translation_agree { get; set; }
    public object translation_bonus_langs { get; set; }
    public bool is_translation_bonus_child { get; set; }
}

public class Va
{
    public string id { get; set; }
    public string name { get; set; }
}

public class ZhCn
{
    public string name { get; set; }
    public List<History> history { get; set; }
}