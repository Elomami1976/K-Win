namespace KWin.Utils
{
    /// <summary>
    /// Strings added in K-Win 1.5 (least privilege, verified restore points, cleanup reports,
    /// history). Merged into the existing tables; entries here override 1.0 strings with the
    /// same key where the old wording was no longer accurate.
    /// Kept separate so it can move into .resx resources together with the rest later.
    /// </summary>
    public static partial class Loc
    {
        private static void RegisterSecurityStrings()
        {
            Merge("en", new Dictionary<string, string>
            {
                ["elev.status_admin"] = "🛡 Running as administrator",
                ["elev.status_user"] = "Standard user · Restart as administrator",
                ["elev.generic_op"] = "Changes that need administrator rights",
                ["elev.required_title"] = "K-win — Administrator rights needed",
                ["elev.required_msg"] = "\"{0}\" changes system-wide settings and needs administrator rights.\n\nRestart K-win as administrator now? Windows will ask for permission. Your undo history is kept.",
                ["elev.not_started"] = "K-win was not restarted as administrator.",

                ["safety.rp_failed_title"] = "K-win — Restore point not created",
                ["safety.rp_failed_continue"] = "A System Restore point could not be created:\n\n{0}\n\nK-win still backs up every registry value it changes, so registry changes can be undone. Changes that are not registry-based may not be reversible.\n\nContinue without a restore point?",
                ["safety.cancelled_no_rp"] = "Cancelled: no restore point could be created.",
                ["safety.done_rp_recent"] = "Done: {0}. Windows reused a restore point from the last 24 hours instead of creating a new one.",
                ["safety.partial_failure_status"] = "{0} change(s) failed. See details.",
                ["safety.partial_failure"] = "\"{0}\" did not complete fully.\n\nChanges applied and recorded for undo: {1}\n\nFailed:\n{2}",
                ["safety.undo_partial_note"] = "Note: part of this action (a power plan or service state) could only be undone before K-win was restarted. Only the registry changes will be reverted.",
                ["safety.error_msg"] = "An error occurred during '{0}':\n\n{1}\n\nRegistry changes made before the error are recorded and can be undone with Undo.",

                ["priv.hosts_download_disabled"] = "Downloading ad-block lists is turned off in this version.\n\nK-win 1.0 downloaded a third-party list from the internet and wrote it into your hosts file without verifying it. This stays off until a verified source is available.\n\nYou can still remove a K-win block added by an earlier version with \"Disable\".",

                ["clean.op.scanning"] = "Scanning...",
                ["clean.op.temp_age_note"] = "Files changed in the last 24 hours are kept (they may still be in use).",
                ["clean.op.irreversible_note"] = "Deleted files cannot be restored by K-win.",
                ["clean.op.browser_keeps_data"] = "Only cache files are removed. Passwords, cookies, history and sessions are kept.",
                ["clean.op.cleaned_files_skipped"] = "Cleaned {0} files, freed {1}. {2} files were skipped (in use or protected).",
                ["clean.op.recycle_title"] = "Empty Recycle Bin",

                ["apps.op.bloat_all_users_optional"] = "Optional (asked next): also remove for all users, so the apps are not installed for new accounts.",
                ["apps.op.bloat_all_users_q"] = "Advanced option: also remove these apps for ALL users on this PC?\n\nThis removes the provisioned packages, so new user accounts won't get them. It needs administrator rights and can only be reversed by reinstalling the apps from the Microsoft Store.\n\nChoose No to remove them for your account only.",
                ["apps.op.remove_all_confirm"] = "Remove {0} app(s) for ALL users on this PC?\n\nThis cannot be undone by K-win. The apps can only be reinstalled from the Microsoft Store.",

                ["hist.dns_revert_hint"] = "To revert, choose \"DHCP (Auto)\".",
                ["hist.backup_at"] = "Backup saved to {0}",
                ["hist.undo_of"] = "Undo: {0}",
            });

            Merge("fr", new Dictionary<string, string>
            {
                ["elev.status_admin"] = "🛡 Exécuté en tant qu'administrateur",
                ["elev.status_user"] = "Utilisateur standard · Redémarrer en administrateur",
                ["elev.generic_op"] = "Modifications nécessitant des droits d'administrateur",
                ["elev.required_title"] = "K-win — Droits d'administrateur requis",
                ["elev.required_msg"] = "« {0} » modifie des paramètres du système et nécessite des droits d'administrateur.\n\nRedémarrer K-win en tant qu'administrateur maintenant ? Windows demandera l'autorisation. Votre historique d'annulation est conservé.",
                ["elev.not_started"] = "K-win n'a pas été redémarré en tant qu'administrateur.",

                ["safety.rp_failed_title"] = "K-win — Point de restauration non créé",
                ["safety.rp_failed_continue"] = "Impossible de créer un point de restauration :\n\n{0}\n\nK-win sauvegarde toujours chaque valeur du registre modifiée ; ces modifications peuvent être annulées. Les modifications hors registre peuvent ne pas être réversibles.\n\nContinuer sans point de restauration ?",
                ["safety.cancelled_no_rp"] = "Annulé : aucun point de restauration n'a pu être créé.",
                ["safety.done_rp_recent"] = "Terminé : {0}. Windows a réutilisé un point de restauration des dernières 24 heures au lieu d'en créer un nouveau.",
                ["safety.partial_failure_status"] = "{0} modification(s) ont échoué. Voir les détails.",
                ["safety.partial_failure"] = "« {0} » ne s'est pas terminé entièrement.\n\nModifications appliquées et annulables : {1}\n\nÉchecs :\n{2}",
                ["safety.undo_partial_note"] = "Remarque : une partie de cette action (mode d'alimentation ou état d'un service) ne pouvait être annulée qu'avant le redémarrage de K-win. Seules les modifications du registre seront annulées.",
                ["safety.error_msg"] = "Une erreur s'est produite lors de « {0} » :\n\n{1}\n\nLes modifications du registre effectuées avant l'erreur sont enregistrées et peuvent être annulées.",

                ["priv.hosts_download_disabled"] = "Le téléchargement de listes de blocage est désactivé dans cette version.\n\nK-win 1.0 téléchargeait une liste tierce sans la vérifier et l'écrivait dans le fichier hosts. Cette fonction reste désactivée jusqu'à ce qu'une source vérifiée soit disponible.\n\nVous pouvez toujours supprimer un bloc ajouté par une ancienne version avec « Désactiver ».",

                ["clean.op.scanning"] = "Analyse...",
                ["clean.op.temp_age_note"] = "Les fichiers modifiés au cours des dernières 24 heures sont conservés (ils peuvent être en cours d'utilisation).",
                ["clean.op.irreversible_note"] = "Les fichiers supprimés ne peuvent pas être restaurés par K-win.",
                ["clean.op.browser_keeps_data"] = "Seuls les fichiers de cache sont supprimés. Mots de passe, cookies, historique et sessions sont conservés.",
                ["clean.op.cleaned_files_skipped"] = "{0} fichiers nettoyés, {1} libérés. {2} fichiers ignorés (en cours d'utilisation ou protégés).",
                ["clean.op.recycle_title"] = "Vider la corbeille",

                ["apps.op.bloat_all_users_optional"] = "Facultatif (demandé ensuite) : supprimer aussi pour tous les utilisateurs.",
                ["apps.op.bloat_all_users_q"] = "Option avancée : supprimer aussi ces applications pour TOUS les utilisateurs de ce PC ?\n\nLes paquets provisionnés seront supprimés et les nouveaux comptes ne les recevront pas. Nécessite des droits d'administrateur ; réversible uniquement en réinstallant depuis le Microsoft Store.\n\nChoisissez Non pour les supprimer uniquement pour votre compte.",
                ["apps.op.remove_all_confirm"] = "Supprimer {0} application(s) pour TOUS les utilisateurs de ce PC ?\n\nK-win ne peut pas annuler cette action. Les applications ne pourront être réinstallées que depuis le Microsoft Store.",

                ["hist.dns_revert_hint"] = "Pour revenir en arrière, choisissez « DHCP (Auto) ».",
                ["hist.backup_at"] = "Sauvegarde enregistrée dans {0}",
                ["hist.undo_of"] = "Annulation : {0}",
            });

            Merge("ar", new Dictionary<string, string>
            {
                ["elev.status_admin"] = "🛡 يعمل بصلاحيات المسؤول",
                ["elev.status_user"] = "مستخدم عادي · إعادة التشغيل كمسؤول",
                ["elev.generic_op"] = "التغييرات التي تتطلب صلاحيات المسؤول",
                ["elev.required_title"] = "K-win — مطلوب صلاحيات المسؤول",
                ["elev.required_msg"] = "\"{0}\" يغيّر إعدادات على مستوى النظام ويتطلب صلاحيات المسؤول.\n\nهل تريد إعادة تشغيل K-win كمسؤول الآن؟ سيطلب ويندوز الإذن. سيتم الاحتفاظ بسجل التراجع.",
                ["elev.not_started"] = "لم تتم إعادة تشغيل K-win كمسؤول.",

                ["safety.rp_failed_title"] = "K-win — لم يتم إنشاء نقطة استعادة",
                ["safety.rp_failed_continue"] = "تعذّر إنشاء نقطة استعادة للنظام:\n\n{0}\n\nلا يزال K-win يحفظ نسخة احتياطية من كل قيمة يغيّرها في السجل، لذا يمكن التراجع عن تغييرات السجل. أما التغييرات الأخرى فقد لا يمكن التراجع عنها.\n\nهل تريد المتابعة بدون نقطة استعادة؟",
                ["safety.cancelled_no_rp"] = "تم الإلغاء: تعذّر إنشاء نقطة استعادة.",
                ["safety.done_rp_recent"] = "تم: {0}. استخدم ويندوز نقطة استعادة من آخر 24 ساعة بدلاً من إنشاء نقطة جديدة.",
                ["safety.partial_failure_status"] = "فشل {0} تغيير/تغييرات. راجع التفاصيل.",
                ["safety.partial_failure"] = "لم تكتمل العملية \"{0}\" بالكامل.\n\nالتغييرات المطبّقة والقابلة للتراجع: {1}\n\nالتغييرات الفاشلة:\n{2}",
                ["safety.undo_partial_note"] = "ملاحظة: جزء من هذا الإجراء (خطة الطاقة أو حالة خدمة) كان يمكن التراجع عنه فقط قبل إعادة تشغيل K-win. سيتم التراجع عن تغييرات السجل فقط.",
                ["safety.error_msg"] = "حدث خطأ أثناء '{0}':\n\n{1}\n\nتغييرات السجل التي تمت قبل الخطأ محفوظة ويمكن التراجع عنها.",

                ["priv.hosts_download_disabled"] = "تنزيل قوائم حظر الإعلانات معطّل في هذا الإصدار.\n\nكان K-win 1.0 ينزّل قائمة من طرف ثالث من الإنترنت ويكتبها في ملف hosts دون التحقق منها. ستبقى هذه الميزة معطّلة حتى يتوفر مصدر موثوق.\n\nلا يزال بإمكانك إزالة قائمة الحظر التي أضافها إصدار سابق باستخدام \"تعطيل\".",

                ["clean.op.scanning"] = "جارٍ الفحص...",
                ["clean.op.temp_age_note"] = "يتم الاحتفاظ بالملفات التي تغيّرت خلال آخر 24 ساعة (قد تكون قيد الاستخدام).",
                ["clean.op.irreversible_note"] = "لا يستطيع K-win استعادة الملفات المحذوفة.",
                ["clean.op.browser_keeps_data"] = "يتم حذف ملفات التخزين المؤقت فقط. كلمات المرور وملفات تعريف الارتباط والسجل والجلسات تبقى كما هي.",
                ["clean.op.cleaned_files_skipped"] = "تم تنظيف {0} ملفات وتحرير {1}. تم تخطي {2} ملفات (قيد الاستخدام أو محمية).",
                ["clean.op.recycle_title"] = "إفراغ سلة المحذوفات",

                ["apps.op.bloat_all_users_optional"] = "اختياري (سيُسأل عنه لاحقاً): الإزالة لجميع المستخدمين أيضاً.",
                ["apps.op.bloat_all_users_q"] = "خيار متقدم: هل تريد إزالة هذه التطبيقات لجميع المستخدمين على هذا الجهاز أيضاً؟\n\nسيتم حذف الحزم المُوفّرة فلن تحصل عليها الحسابات الجديدة. يتطلب صلاحيات المسؤول، ولا يمكن التراجع عنه إلا بإعادة التثبيت من متجر Microsoft.\n\nاختر \"لا\" للإزالة من حسابك فقط.",
                ["apps.op.remove_all_confirm"] = "هل تريد إزالة {0} تطبيق/تطبيقات لجميع المستخدمين على هذا الجهاز؟\n\nلا يستطيع K-win التراجع عن ذلك. لا يمكن إعادة التطبيقات إلا بتثبيتها من متجر Microsoft.",

                ["hist.dns_revert_hint"] = "للتراجع، اختر \"DHCP (تلقائي)\".",
                ["hist.backup_at"] = "تم حفظ النسخة الاحتياطية في {0}",
                ["hist.undo_of"] = "تراجع: {0}",
            });
        }

        /// <summary>
        /// Keeps numbers with units (e.g. "358.5 GB / 673.6 GB") in left-to-right order inside
        /// Arabic text, using a Unicode left-to-right embedding.
        /// </summary>
        public static string Ltr(string text) => IsRTL ? "\u202A" + text + "\u202C" : text;

        /// <summary>True if the language table defines the key (used by tests).</summary>
        internal static bool HasKey(string lang, string key) =>
            _translations.TryGetValue(lang, out var d) && d.ContainsKey(key);

        /// <summary>Keys added in 1.5 (security + shell strings), for completeness tests.</summary>
        internal static IReadOnlyCollection<string> Keys15
        {
            get
            {
                var keys = new HashSet<string>();
                foreach (var m in _keys15) keys.Add(m);
                return keys;
            }
        }

        private static readonly List<string> _keys15 = new();

        private static void Merge(string lang, Dictionary<string, string> strings)
        {
            if (!_translations.TryGetValue(lang, out var dict))
            {
                dict = new Dictionary<string, string>();
                _translations[lang] = dict;
            }
            foreach (var (key, value) in strings)
            {
                dict[key] = value;
                if (lang == "en") _keys15.Add(key);
            }
        }
    }
}
