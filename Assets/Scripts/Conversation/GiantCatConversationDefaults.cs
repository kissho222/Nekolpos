namespace Backgammon.Conversation
{
    public static class GiantCatConversationDefaults
    {
        public static ConversationParseConfig CreateConfig()
        {
            return new ConversationParseConfig
            {
                intentRules =
                {
                    new RegexIntentRule { pattern = "(なで(て|たい|させて)|もふ(らせて|りたい)|さわらせて)", intent = "request_pet", priority = 120 },
                    new RegexIntentRule { pattern = "(たべたい|ごはん|えさ|えさちょうだい|おやつ|おなか(が)?すいた)", intent = "request_food", priority = 100 },
                    new RegexIntentRule { pattern = "(あそぼ|あそんで|かまって|ひま)", intent = "request_play", priority = 90 },
                    new RegexIntentRule { pattern = "(おいで|こっちきて|ついてきて)", intent = "request_follow", priority = 85 },
                    new RegexIntentRule { pattern = "(おはよう|こんにちは|こんばんは|やあ)", intent = "greeting", priority = 70 },
                    new RegexIntentRule { pattern = "(ねむい|ねかせて|おやすみ|いっしょにねたい)", intent = "request_sleep", priority = 65 },
                    new RegexIntentRule { pattern = "(かわいい|えらい|すごい)", intent = "praise", priority = 60 },
                    new RegexIntentRule { pattern = "(ごめん|すまん|ゆるして)", intent = "apology", priority = 55 }
                },
                categoryRules =
                {
                    new KeywordCategoryRule { word = "おさかな", type = "food_category", value = "Fish", priority = 120 },
                    new KeywordCategoryRule { word = "さかな", type = "food_category", value = "Fish", priority = 110 },
                    new KeywordCategoryRule { word = "まぐろ", type = "food_category", value = "Fish", priority = 110 },
                    new KeywordCategoryRule { word = "しゃけ", type = "food_category", value = "Fish", priority = 110 },
                    new KeywordCategoryRule { word = "さーもん", type = "food_category", value = "Fish", priority = 110 },
                    new KeywordCategoryRule { word = "にぼし", type = "food_category", value = "Fish", priority = 110 },
                    new KeywordCategoryRule { word = "つな", type = "food_category", value = "Fish", priority = 105 },
                    new KeywordCategoryRule { word = "かつお", type = "food_category", value = "Fish", priority = 105 },
                    new KeywordCategoryRule { word = "とりにく", type = "food_category", value = "Meat", priority = 100 },
                    new KeywordCategoryRule { word = "ささみ", type = "food_category", value = "Meat", priority = 100 },
                    new KeywordCategoryRule { word = "にくきゅう", type = "touch_category", value = "PawPad", priority = 120 },
                    new KeywordCategoryRule { word = "肉球", type = "touch_category", value = "PawPad", priority = 120 },
                    new KeywordCategoryRule { word = "にく", type = "food_category", value = "Meat", priority = 90 },
                    new KeywordCategoryRule { word = "おやつ", type = "food_category", value = "Snack", priority = 100 },
                    new KeywordCategoryRule { word = "ちゅーる", type = "food_category", value = "Snack", priority = 110 },
                    new KeywordCategoryRule { word = "みず", type = "drink_category", value = "Water", priority = 90 },
                    new KeywordCategoryRule { word = "みるく", type = "drink_category", value = "Milk", priority = 90 },
                    new KeywordCategoryRule { word = "なで", type = "touch_category", value = "Petting", priority = 100 },
                    new KeywordCategoryRule { word = "もふ", type = "touch_category", value = "Petting", priority = 100 }
                }
            };
        }
    }
}
