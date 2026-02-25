// ===== K-win Landing Page Script =====

document.addEventListener('DOMContentLoaded', () => {
    initNavbar();
    initMobileMenu();
    initFAQ();
    initScreenshotTabs();
    initScrollReveal();
});

// ===== NAVBAR SCROLL EFFECT =====
function initNavbar() {
    const nav = document.querySelector('.nav');
    if (!nav) return;

    const onScroll = () => {
        nav.classList.toggle('scrolled', window.scrollY > 40);
    };

    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
}

// ===== MOBILE MENU TOGGLE =====
function initMobileMenu() {
    const toggle = document.querySelector('.nav-toggle');
    const navLinks = document.querySelector('.nav-links');
    if (!toggle || !navLinks) return;

    toggle.addEventListener('click', () => {
        navLinks.classList.toggle('open');
        const spans = toggle.querySelectorAll('span');
        if (navLinks.classList.contains('open')) {
            spans[0].style.transform = 'rotate(45deg) translate(5px, 5px)';
            spans[1].style.opacity = '0';
            spans[2].style.transform = 'rotate(-45deg) translate(5px, -5px)';
        } else {
            spans[0].style.transform = '';
            spans[1].style.opacity = '';
            spans[2].style.transform = '';
        }
    });

    // Close menu on link click
    navLinks.querySelectorAll('a').forEach(link => {
        link.addEventListener('click', () => {
            navLinks.classList.remove('open');
            const spans = toggle.querySelectorAll('span');
            spans.forEach(s => { s.style.transform = ''; s.style.opacity = ''; });
        });
    });
}

// ===== FAQ ACCORDION =====
function initFAQ() {
    const items = document.querySelectorAll('.faq-item');

    items.forEach(item => {
        const question = item.querySelector('.faq-question');
        if (!question) return;

        question.addEventListener('click', () => {
            const isOpen = item.classList.contains('open');

            // Close all
            items.forEach(i => i.classList.remove('open'));

            // Open clicked (if it wasn't already open)
            if (!isOpen) {
                item.classList.add('open');
            }
        });
    });
}

// ===== SCREENSHOT TABS =====
function initScreenshotTabs() {
    const tabs = document.querySelectorAll('.ss-tab');
    const contents = document.querySelectorAll('.placeholder-content');
    if (!tabs.length || !contents.length) return;

    tabs.forEach(tab => {
        tab.addEventListener('click', () => {
            const target = tab.dataset.tab;

            // Update active tab
            tabs.forEach(t => t.classList.remove('active'));
            tab.classList.add('active');

            // Show matching content
            contents.forEach(c => {
                if (c.id === target) {
                    c.classList.remove('hidden');
                } else {
                    c.classList.add('hidden');
                }
            });
        });
    });
}

// ===== SCROLL REVEAL ANIMATION =====
function initScrollReveal() {
    const revealElements = document.querySelectorAll(
        '.feature-card, .safety-card, .safety-banner, .download-card, .faq-item'
    );

    if (!revealElements.length) return;

    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.style.opacity = '1';
                entry.target.style.transform = 'translateY(0)';
                observer.unobserve(entry.target);
            }
        });
    }, {
        threshold: 0.1,
        rootMargin: '0px 0px -40px 0px'
    });

    revealElements.forEach((el, i) => {
        el.style.opacity = '0';
        el.style.transform = 'translateY(24px)';
        el.style.transition = `opacity 0.5s ease ${i % 3 * 0.1}s, transform 0.5s ease ${i % 3 * 0.1}s`;
        observer.observe(el);
    });
}

// ===== LANGUAGE TRANSLATIONS =====
const translations = {
    en: {
        'nav.features': 'Features',
        'nav.safety': 'Safety',
        'nav.screenshots': 'Screenshots',
        'nav.faq': 'FAQ',
        'nav.download': 'Download Free',
        'hero.badge': 'Free & Open Source · 🌍 Multilingual',
        'hero.title': 'Optimize <span class="gradient-text">Windows 11</span><br>in One Click',
        'hero.subtitle': 'Boost performance, protect privacy, and clean up disk space — safely and reversibly. No bloatware, no third-party tools, no risk.',
        'hero.downloadBtn': 'Download K-win v1.0.0',
        'hero.learnMore': 'Learn More',
        'hero.stats.optimizations': 'Optimizations',
        'hero.stats.tabs': 'Tabs',
        'hero.stats.languages': 'Languages',
        'hero.stats.reversible': 'Reversible',
        'features.badge': 'Features',
        'features.title': 'Everything You Need.<br>Nothing You Don\'t.',
        'features.subtitle': 'K-win focuses on doing 10 things perfectly rather than 100 things poorly.',
        'safety.badge': 'Safety First',
        'safety.title': 'Every Change is Reversible',
        'safety.subtitle': 'K-win was built with safety as a non-negotiable requirement. Your system is always protected.',
        'screenshots.badge': 'Interface',
        'screenshots.title': 'Clean, Professional UI',
        'screenshots.subtitle': 'Windows 11 native look with automatic dark/light mode detection.',
        'download.title': 'Download K-win',
        'download.subtitle': 'Free. No signup. No bloatware. Just a single EXE file.',
        'faq.badge': 'FAQ',
        'faq.title': 'Frequently Asked Questions',
        'footer.tagline': 'Windows 11 Optimization Tool'
    },
    ar: {
        'nav.features': 'المميزات',
        'nav.safety': 'الأمان',
        'nav.screenshots': 'الواجهة',
        'nav.faq': 'الأسئلة الشائعة',
        'nav.download': 'تحميل مجاني',
        'hero.badge': 'مجاني ومفتوح المصدر · 🌍 متعدد اللغات',
        'hero.title': 'حسّن <span class="gradient-text">ويندوز 11</span><br>بضغطة واحدة',
        'hero.subtitle': 'عزز الأداء، احمِ خصوصيتك، ونظّف مساحة القرص — بأمان وقابلية للتراجع. بدون برامج إضافية، بدون أدوات خارجية، بدون مخاطر.',
        'hero.downloadBtn': 'تحميل K-win v1.0.0',
        'hero.learnMore': 'اعرف المزيد',
        'hero.stats.optimizations': 'تحسين',
        'hero.stats.tabs': 'تبويبات',
        'hero.stats.languages': 'لغات',
        'hero.stats.reversible': 'قابل للتراجع',
        'features.badge': 'المميزات',
        'features.title': 'كل ما تحتاجه.<br>لا شيء لا تحتاجه.',
        'features.subtitle': 'K-win يركز على إتقان 10 أشياء بدلاً من 100 شيء بشكل سيء.',
        'safety.badge': 'الأمان أولاً',
        'safety.title': 'كل تغيير قابل للتراجع',
        'safety.subtitle': 'K-win بُني مع اعتبار الأمان متطلباً غير قابل للتفاوض. نظامك محمي دائماً.',
        'screenshots.badge': 'الواجهة',
        'screenshots.title': 'واجهة نظيفة واحترافية',
        'screenshots.subtitle': 'مظهر ويندوز 11 الأصلي مع كشف تلقائي للوضع الداكن/الفاتح.',
        'download.title': 'تحميل K-win',
        'download.subtitle': 'مجاني. بدون تسجيل. بدون برامج إضافية. مجرد ملف EXE واحد.',
        'faq.badge': 'الأسئلة الشائعة',
        'faq.title': 'الأسئلة المتكررة',
        'footer.tagline': 'أداة تحسين ويندوز 11'
    }
};

// ===== LANGUAGE SWITCHER =====
let currentLang = localStorage.getItem('kwin-lang') || 'en';

function initLanguageSwitcher() {
    const switcher = document.getElementById('langSwitcher');
    if (!switcher) return;

    // Set initial state
    applyLanguage(currentLang);
    updateSwitcherText(switcher);

    switcher.addEventListener('click', () => {
        currentLang = currentLang === 'en' ? 'ar' : 'en';
        localStorage.setItem('kwin-lang', currentLang);
        applyLanguage(currentLang);
        updateSwitcherText(switcher);
    });
}

function updateSwitcherText(switcher) {
    switcher.textContent = currentLang === 'en' ? '🌐 عربي' : '🌐 EN';
}

function applyLanguage(lang) {
    const html = document.documentElement;
    
    // Set direction for Arabic
    if (lang === 'ar') {
        html.setAttribute('dir', 'rtl');
        html.setAttribute('lang', 'ar');
    } else {
        html.setAttribute('dir', 'ltr');
        html.setAttribute('lang', 'en');
    }

    // Apply translations
    document.querySelectorAll('[data-i18n]').forEach(el => {
        const key = el.getAttribute('data-i18n');
        if (translations[lang] && translations[lang][key]) {
            el.innerHTML = translations[lang][key];
        }
    });
}

// Initialize language switcher on load
document.addEventListener('DOMContentLoaded', () => {
    initLanguageSwitcher();
});
