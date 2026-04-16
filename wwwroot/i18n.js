// Локализация интерфейса
const translations = {
    ru: {
        // Главный экран
        'app.title': 'Awakened Steam Desktop Authenticator',
        'accounts.title': 'Аккаунты',
        'accounts.search': 'Поиск',
        'accounts.noAccounts': 'Нет аккаунтов',
        'accounts.notFound': 'Аккаунты не найдены',
        'accounts.addAccount': 'Добавить аккаунт',
        'groups.all': 'Все группы',
        'groups.favorites': 'Избранное',

        // Кнопки действий
        'btn.copy': 'Копировать код',
        'btn.confirmations': 'Подтверждения',
        'btn.settings': 'Настройки',
        'btn.wallet': 'Баланс аккаунта',
        'btn.close': 'Закрыть',
        'btn.save': 'Сохранить',
        'btn.cancel': 'Отмена',
        'btn.add': 'Добавить',
        'btn.remove': 'Удалить',
        'btn.export': 'Экспортировать в Excel',
        'btn.refresh': 'Обновить',
        'btn.create': 'Создать',
        'btn.next': 'Далее',
        'btn.login': 'Войти',
        'btn.accept': 'Принять',
        'btn.deny': 'Отклонить',

        // Контекстное меню
        'context.settings': 'Настройки',
        'context.refreshSession': 'Обновить сессию',
        'context.steamProfile': 'Профиль Steam',
        'context.removeAccount': 'Удалить аккаунт',

        // Тултипы тулбара
        'toolbar.addAccount': 'Добавить аккаунт',
        'toolbar.settings': 'Настройки',
        'toolbar.createGroup': 'Создать группу',
        'toolbar.confirmations': 'Подтверждения',
        'toolbar.trades': 'Торговые предложения',
        'toolbar.market': 'Маркет',

        // Модальные окна
        'modal.settings.title': 'Настройки',
        'modal.settings.general': 'Общие',
        'modal.settings.language': 'Язык интерфейса',
        'modal.settings.defaultGroup': 'Стандартная группа при добавлении аккаунта',
        'modal.settings.hideLogins': 'Скрывать логины (показывать только первые и последние символы)',
        'modal.settings.export': 'Экспорт данных',
        'modal.settings.proxy': 'Прокси',
        'modal.settings.proxyName': 'Название',
        'modal.settings.proxyAddress': 'Адрес (host:port)',
        'modal.settings.proxyUsername': 'Логин',
        'modal.settings.proxyPassword': 'Пароль',
        'modal.settings.proxyActive': 'Активен',

        'modal.account.title': 'Настройки аккаунта',
        'modal.account.username': 'Имя пользователя',
        'modal.account.group': 'Группа',
        'modal.account.proxy': 'Прокси',
        'modal.account.noProxy': 'Без прокси',
        'modal.account.revocationCode': 'Код отзыва',
        'modal.account.removeAccount': 'Удалить аккаунт',
        'modal.account.autoTrade': 'Автоматическое принятие трейдов',
        'modal.account.autoMarket': 'Автоматическое принятие маркета',
        'modal.account.refreshSession': 'Обновить сессию',

        'modal.confirmations.title': 'Подтверждения',
        'modal.confirmations.noConfirmations': 'Нет подтверждений',
        'modal.confirmations.acceptAll': 'Принять все',
        'modal.confirmations.cancelAll': 'Отклонить все',
        'modal.confirmations.loading': 'Загрузка подтверждений...',
        'modal.confirmations.nothingToConfirm': 'Нечего подтверждать',
        'modal.confirmations.noDescription': 'Без описания',
        'modal.confirmations.typeTrade': 'Трейд',
        'modal.confirmations.typeMarket': 'Маркет',
        'modal.confirmations.typeLogin': 'Вход',
        'modal.confirmations.typeGeneral': 'Общее',
        'modal.confirmations.typeConfirmation': 'Подтверждение',

        'modal.trades.title': 'Торговые предложения',
        'modal.trades.noTrades': 'Нет торговых предложений',

        'modal.market.title': 'Маркет',
        'modal.market.noListings': 'Нет активных листингов',

        'modal.addAccount.title': 'Добавить аккаунт',
        'modal.addAccount.username': 'Логин Steam',
        'modal.addAccount.password': 'Пароль',
        'modal.addAccount.emailCode': 'Код с почты',
        'modal.addAccount.guardCode': 'Код Steam Guard',
        'modal.addAccount.group': 'Группа',
        'modal.addAccount.step1': 'Шаг 1: Авторизация',
        'modal.addAccount.step2': 'Шаг 2: Код с почты',
        'modal.addAccount.step3': 'Шаг 3: Код Steam Guard',
        'modal.addAccount.step4': 'Шаг 4: Код восстановления',
        'modal.addAccount.loginPlaceholder': 'Введите логин Steam',
        'modal.addAccount.passwordPlaceholder': 'Введите пароль',
        'modal.addAccount.emailCodePlaceholder': 'Введите код из письма',
        'modal.addAccount.guardCodePlaceholder': 'Введите код из письма',
        'modal.addAccount.emailWarning': 'Введите код подтверждения из электронной почты',
        'modal.addAccount.guardWarning': 'Введите код для подключения Steam Guard',
        'modal.addAccount.revocationWarning': 'Сохраните этот код! Он необходим для восстановления аккаунта.',
        'modal.addAccount.clickToCopy': 'Нажмите на код, чтобы скопировать',

        'modal.password.title': 'Требуется пароль',
        'modal.password.message': 'Введите пароль для обновления сессии',
        'modal.password.label': 'Пароль',
        'modal.password.placeholder': 'Введите пароль',

        'modal.createGroup.title': 'Создать группу',
        'modal.createGroup.name': 'Название группы',
        'modal.createGroup.placeholder': 'Введите название группы',

        'modal.addProxy.title': 'Добавить прокси',
        'modal.addProxy.name': 'Название',
        'modal.addProxy.namePlaceholder': 'Мой прокси',
        'modal.addProxy.address': 'Адрес (host:port)',
        'modal.addProxy.addressPlaceholder': '127.0.0.1:8080',
        'modal.addProxy.username': 'Логин (необязательно)',
        'modal.addProxy.usernamePlaceholder': 'user',
        'modal.addProxy.password': 'Пароль (необязательно)',
        'modal.addProxy.passwordPlaceholder': 'password',

        // Прокси
        'proxy.notAdded': 'Прокси не добавлены',

        // Уведомления
        'toast.codeCopied': 'Код скопирован',
        'toast.saved': 'Сохранено',
        'toast.error': 'Ошибка',
        'toast.loading': 'Загрузка...',
        'toast.steamIdNotFound': 'SteamID не найден для этого аккаунта',
        'toast.refreshingSession': 'Обновление сессии...',
        'toast.fillAllFields': 'Заполните все поля',
        'toast.enterCode': 'Введите код',
        'toast.dataCopied': 'Данные скопированы в буфер обмена. Вставьте в Excel через Ctrl+V',
        'toast.copyError': 'Ошибка копирования',
        'toast.fillProxyFields': 'Заполните название и адрес прокси',
        'toast.enterGroupName': 'Введите название группы',
        'toast.accountAdded': 'Аккаунт добавлен!',
        'toast.accountImported': 'Аккаунт импортирован!',
        'toast.unknownError': 'Неизвестная ошибка',
        'toast.balanceError': 'Не удалось получить баланс',
        'toast.settingsSaved': 'Настройки сохранены',
        'toast.accountSettingsSaved': 'Настройки сохранены',
        'toast.groupCreated': 'Группа создана',
        'toast.accountRemoved': 'Аккаунт удалён',
        'toast.sessionRefreshed': 'Сессия обновлена',
        'toast.authError': 'Ошибка авторизации',
        'toast.confirmationAccepted': 'Подтверждение принято',
        'toast.confirmationDenied': 'Подтверждение отклонено',
        'toast.allConfirmationsAccepted': 'Все подтверждения приняты',
        'toast.confirmationsError': 'Ошибка при принятии подтверждений',
        'toast.tradeAccepted': 'Трейд принят',
        'toast.tradeDeclined': 'Трейд отклонён',
        'toast.tradeAccepting': 'Принятие трейда...',
        'toast.tradeDeclining': 'Отклонение трейда...',
        'toast.tradeCancelling': 'Отмена трейда...',
        'toast.tradeAcceptError': 'Не удалось принять трейд',
        'toast.tradeDeclineError': 'Не удалось отклонить трейд',
        'toast.listingCancelled': 'Листинг отменён',
        'toast.listingCancelling': 'Отмена листинга...',
        'toast.listingCancelError': 'Не удалось отменить листинг',
        'toast.codeCopiedWith': 'Код скопирован',
        'toast.balanceFetched': 'Баланс получен',
        'toast.balanceFetchError': 'Ошибка получения баланса',
        'toast.usernameCopied': 'Скопировано',
        'toast.rcodeCopied': 'R-код скопирован',
        'toast.sessionRefreshError': 'Не удалось обновить сессию',
        'toast.accountNotFound': 'Аккаунт не найден',
        'toast.enterPasswordForBalance': 'Введите пароль для получения баланса',
        'toast.codeGenerationError': 'Ошибка генерации кода',
        'toast.loginPasswordRequired': 'Логин и пароль не могут быть пустыми',
        'toast.enterEmailCode': 'Введите код с почты',
        'toast.enterGuardCode': 'Введите код подтверждения (SMS или Email)',
        'toast.unsupportedConfirmationType': 'Неподдерживаемый тип подтверждения',
        'toast.emailConfirmError': 'Ошибка подтверждения email',
        'toast.addAuthenticatorError': 'Не удалось добавить аутентификатор',
        'toast.noAuthData': 'Ошибка: нет данных для авторизации',
        'toast.noAuthenticatorData': 'Ошибка: нет данных аутентификатора',
        'toast.finalizeAuthenticatorError': 'Не удалось финализировать аутентификатор',
        'toast.enterPassword': 'Введите пароль',
        'toast.copied': 'Скопировано',

        // Подтверждения
        'confirm.removeAccount': 'Удалить аккаунт',

        // Шаги добавления аккаунта
        'step.authorization': 'Шаг 1: Авторизация',
        'step.emailCode': 'Шаг 2: Код с почты',
        'step.guardCode': 'Шаг 3: Код Steam Guard',
        'step.accountAdded': 'Аккаунт добавлен!',
        'step.confirm': 'Подтвердить',
        'step.connect': 'Подключить',

        // Экспорт
        'export.headers.login': 'Логин',
        'export.headers.password': 'Пароль',
        'export.headers.steamId': 'Steam ID',
        'export.headers.rcode': 'Rcode',
        'export.headers.balance': 'Баланс',

        // Тултипы
        'tooltip.balance': 'Баланс аккаунта',
        'tooltip.copyCode': 'Копировать код',
        'tooltip.edit': 'Редактировать',
        'tooltip.activate': 'Активировать',
        'tooltip.deactivate': 'Деактивировать',
        'tooltip.delete': 'Удалить',
        'tooltip.autoTrade': 'Авто трейд',
        'tooltip.autoMarket': 'Авто маркет',
        'tooltip.profile': 'Профиль Steam',
        'tooltip.accountSettings': 'Настройки аккаунта',

        // Время
        'time.justNow': 'Только что',
        'time.minutesAgo': 'мин. назад',
        'time.hoursAgo': 'ч. назад',
        'time.daysAgo': 'дн. назад',
    },
    en: {
        // Main screen
        'app.title': 'Awakened Steam Desktop Authenticator',
        'accounts.title': 'Accounts',
        'accounts.search': 'Search',
        'accounts.noAccounts': 'No accounts',
        'accounts.notFound': 'Accounts not found',
        'accounts.addAccount': 'Add Account',
        'groups.all': 'All groups',
        'groups.favorites': 'Favorites',

        // Action buttons
        'btn.copy': 'Copy code',
        'btn.confirmations': 'Confirmations',
        'btn.settings': 'Settings',
        'btn.wallet': 'Account balance',
        'btn.close': 'Close',
        'btn.save': 'Save',
        'btn.cancel': 'Cancel',
        'btn.add': 'Add',
        'btn.remove': 'Remove',
        'btn.export': 'Export to Excel',
        'btn.refresh': 'Refresh',
        'btn.create': 'Create',
        'btn.next': 'Next',
        'btn.login': 'Login',
        'btn.accept': 'Accept',
        'btn.deny': 'Deny',

        // Context menu
        'context.settings': 'Settings',
        'context.refreshSession': 'Refresh session',
        'context.steamProfile': 'Steam Profile',
        'context.removeAccount': 'Remove Account',

        // Toolbar tooltips
        'toolbar.addAccount': 'Add Account',
        'toolbar.settings': 'Settings',
        'toolbar.createGroup': 'Create Group',
        'toolbar.confirmations': 'Confirmations',
        'toolbar.trades': 'Trade Offers',
        'toolbar.market': 'Market',

        // Modals
        'modal.settings.title': 'Settings',
        'modal.settings.general': 'General',
        'modal.settings.language': 'Interface Language',
        'modal.settings.defaultGroup': 'Default Group',
        'modal.settings.hideLogins': 'Hide logins (show only first and last characters)',
        'modal.settings.export': 'Export Data',
        'modal.settings.proxy': 'Proxy',
        'modal.settings.proxyName': 'Name',
        'modal.settings.proxyAddress': 'Address (host:port)',
        'modal.settings.proxyUsername': 'Username',
        'modal.settings.proxyPassword': 'Password',
        'modal.settings.proxyActive': 'Active',

        'modal.account.title': 'Account Settings',
        'modal.account.username': 'Username',
        'modal.account.group': 'Group',
        'modal.account.proxy': 'Proxy',
        'modal.account.noProxy': 'No proxy',
        'modal.account.revocationCode': 'Revocation Code',
        'modal.account.removeAccount': 'Remove Account',
        'modal.account.autoTrade': 'Auto-accept trades',
        'modal.account.autoMarket': 'Auto-accept market',
        'modal.account.refreshSession': 'Refresh session',

        'modal.confirmations.title': 'Confirmations',
        'modal.confirmations.noConfirmations': 'No confirmations',
        'modal.confirmations.acceptAll': 'Accept All',
        'modal.confirmations.cancelAll': 'Cancel All',
        'modal.confirmations.loading': 'Loading confirmations...',
        'modal.confirmations.nothingToConfirm': 'Nothing to confirm',
        'modal.confirmations.noDescription': 'No description',
        'modal.confirmations.typeTrade': 'Trade',
        'modal.confirmations.typeMarket': 'Market',
        'modal.confirmations.typeLogin': 'Login',
        'modal.confirmations.typeGeneral': 'General',
        'modal.confirmations.typeConfirmation': 'Confirmation',

        'modal.trades.title': 'Trade Offers',
        'modal.trades.noTrades': 'No trade offers',

        'modal.market.title': 'Market',
        'modal.market.noListings': 'No active listings',

        'modal.addAccount.title': 'Add Account',
        'modal.addAccount.username': 'Steam Login',
        'modal.addAccount.password': 'Password',
        'modal.addAccount.emailCode': 'Email Code',
        'modal.addAccount.guardCode': 'Steam Guard Code',
        'modal.addAccount.group': 'Group',
        'modal.addAccount.step1': 'Step 1: Authorization',
        'modal.addAccount.step2': 'Step 2: Email Code',
        'modal.addAccount.step3': 'Step 3: Steam Guard Code',
        'modal.addAccount.step4': 'Step 4: Recovery Code',
        'modal.addAccount.loginPlaceholder': 'Enter Steam login',
        'modal.addAccount.passwordPlaceholder': 'Enter password',
        'modal.addAccount.emailCodePlaceholder': 'Enter code from email',
        'modal.addAccount.guardCodePlaceholder': 'Enter code from email',
        'modal.addAccount.emailWarning': 'Enter confirmation code from email',
        'modal.addAccount.guardWarning': 'Enter code to enable Steam Guard',
        'modal.addAccount.revocationWarning': 'Save this code! It is required for account recovery.',
        'modal.addAccount.clickToCopy': 'Click on code to copy',

        'modal.password.title': 'Password Required',
        'modal.password.message': 'Enter password to refresh session',
        'modal.password.label': 'Password',
        'modal.password.placeholder': 'Enter password',

        'modal.createGroup.title': 'Create Group',
        'modal.createGroup.name': 'Group Name',
        'modal.createGroup.placeholder': 'Enter group name',

        'modal.addProxy.title': 'Add Proxy',
        'modal.addProxy.name': 'Name',
        'modal.addProxy.namePlaceholder': 'My proxy',
        'modal.addProxy.address': 'Address (host:port)',
        'modal.addProxy.addressPlaceholder': '127.0.0.1:8080',
        'modal.addProxy.username': 'Username (optional)',
        'modal.addProxy.usernamePlaceholder': 'user',
        'modal.addProxy.password': 'Password (optional)',
        'modal.addProxy.passwordPlaceholder': 'password',

        // Proxy
        'proxy.notAdded': 'No proxies added',

        // Notifications
        'toast.codeCopied': 'Code copied',
        'toast.saved': 'Saved',
        'toast.error': 'Error',
        'toast.loading': 'Loading...',
        'toast.steamIdNotFound': 'SteamID not found for this account',
        'toast.refreshingSession': 'Refreshing session...',
        'toast.fillAllFields': 'Fill all fields',
        'toast.enterCode': 'Enter code',
        'toast.dataCopied': 'Data copied to clipboard. Paste into Excel with Ctrl+V',
        'toast.copyError': 'Copy error',
        'toast.fillProxyFields': 'Fill proxy name and address',
        'toast.enterGroupName': 'Enter group name',
        'toast.accountAdded': 'Account added!',
        'toast.accountImported': 'Account imported!',
        'toast.unknownError': 'Unknown error',
        'toast.balanceError': 'Failed to get balance',
        'toast.settingsSaved': 'Settings saved',
        'toast.accountSettingsSaved': 'Settings saved',
        'toast.groupCreated': 'Group created',
        'toast.accountRemoved': 'Account removed',
        'toast.sessionRefreshed': 'Session refreshed',
        'toast.authError': 'Authentication error',
        'toast.confirmationAccepted': 'Confirmation accepted',
        'toast.confirmationDenied': 'Confirmation denied',
        'toast.allConfirmationsAccepted': 'All confirmations accepted',
        'toast.confirmationsError': 'Error accepting confirmations',
        'toast.tradeAccepted': 'Trade accepted',
        'toast.tradeDeclined': 'Trade declined',
        'toast.tradeAccepting': 'Accepting trade...',
        'toast.tradeDeclining': 'Declining trade...',
        'toast.tradeCancelling': 'Cancelling trade...',
        'toast.tradeAcceptError': 'Failed to accept trade',
        'toast.tradeDeclineError': 'Failed to decline trade',
        'toast.listingCancelled': 'Listing cancelled',
        'toast.listingCancelling': 'Cancelling listing...',
        'toast.listingCancelError': 'Failed to cancel listing',
        'toast.codeCopiedWith': 'Code copied',
        'toast.balanceFetched': 'Balance fetched',
        'toast.balanceFetchError': 'Balance fetch error',
        'toast.usernameCopied': 'Copied',
        'toast.rcodeCopied': 'R-code copied',
        'toast.sessionRefreshError': 'Failed to refresh session',
        'toast.accountNotFound': 'Account not found',
        'toast.enterPasswordForBalance': 'Enter password to get balance',
        'toast.codeGenerationError': 'Code generation error',
        'toast.loginPasswordRequired': 'Login and password cannot be empty',
        'toast.enterEmailCode': 'Enter email code',
        'toast.enterGuardCode': 'Enter confirmation code (SMS or Email)',
        'toast.unsupportedConfirmationType': 'Unsupported confirmation type',
        'toast.emailConfirmError': 'Email confirmation error',
        'toast.addAuthenticatorError': 'Failed to add authenticator',
        'toast.noAuthData': 'Error: no authorization data',
        'toast.noAuthenticatorData': 'Error: no authenticator data',
        'toast.finalizeAuthenticatorError': 'Failed to finalize authenticator',
        'toast.enterPassword': 'Enter password',
        'toast.copied': 'Copied',

        // Confirmations
        'confirm.removeAccount': 'Remove account',

        // Add account steps
        'step.authorization': 'Step 1: Authorization',
        'step.emailCode': 'Step 2: Email Code',
        'step.guardCode': 'Step 3: Steam Guard Code',
        'step.accountAdded': 'Account added!',
        'step.confirm': 'Confirm',
        'step.connect': 'Connect',

        // Export
        'export.headers.login': 'Login',
        'export.headers.password': 'Password',
        'export.headers.steamId': 'Steam ID',
        'export.headers.rcode': 'Rcode',
        'export.headers.balance': 'Balance',

        // Tooltips
        'tooltip.balance': 'Account balance',
        'tooltip.copyCode': 'Copy code',
        'tooltip.edit': 'Edit',
        'tooltip.activate': 'Activate',
        'tooltip.deactivate': 'Deactivate',
        'tooltip.delete': 'Delete',
        'tooltip.autoTrade': 'Auto trade',
        'tooltip.autoMarket': 'Auto market',
        'tooltip.profile': 'Steam Profile',
        'tooltip.accountSettings': 'Account settings',

        // Time
        'time.justNow': 'Just now',
        'time.minutesAgo': 'min. ago',
        'time.hoursAgo': 'h. ago',
        'time.daysAgo': 'd. ago',
    }
};

// Текущий язык (по умолчанию русский)
let currentLanguage = 'ru';

// Функция перевода
function t(key) {
    return translations[currentLanguage][key] || key;
}

// Функция смены языка
function setLanguage(lang) {
    if (!translations[lang]) {
        console.error(`Language ${lang} not found`);
        return;
    }
    currentLanguage = lang;
    localStorage.setItem('language', lang);
    updateUILanguage();
}

// Обновление всех текстов в UI
function updateUILanguage() {
    // Обновляем все элементы с data-i18n атрибутом
    document.querySelectorAll('[data-i18n]').forEach(el => {
        const key = el.getAttribute('data-i18n');
        const translation = t(key);

        if (el.tagName === 'INPUT' && (el.type === 'text' || el.type === 'password')) {
            el.placeholder = translation;
        } else {
            el.textContent = translation;
        }
    });

    // Обновляем placeholder'ы с data-i18n-placeholder атрибутом
    document.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
        const key = el.getAttribute('data-i18n-placeholder');
        const translation = t(key);
        el.placeholder = translation;
    });

    // Обновляем тултипы тулбара
    updateToolbarTooltips();
}

// Функция для обновления тултипов тулбара
function updateToolbarTooltips() {
    // Эта функция будет вызвана из index.html после инициализации тултипов
    if (typeof setupTooltip !== 'undefined') {
        document.querySelectorAll('.toolbar-btn-add').forEach(btn => {
            btn.setAttribute('data-tooltip-key', 'toolbar.addAccount');
        });
        document.querySelectorAll('.toolbar-btn-settings').forEach(btn => {
            btn.setAttribute('data-tooltip-key', 'toolbar.settings');
        });
        document.querySelectorAll('.toolbar-btn-create-group').forEach(btn => {
            btn.setAttribute('data-tooltip-key', 'toolbar.createGroup');
        });
        document.querySelectorAll('.toolbar-btn-confirmations').forEach(btn => {
            btn.setAttribute('data-tooltip-key', 'toolbar.confirmations');
        });
        document.querySelectorAll('.toolbar-btn-trades').forEach(btn => {
            btn.setAttribute('data-tooltip-key', 'toolbar.trades');
        });
        document.querySelectorAll('.toolbar-btn-market').forEach(btn => {
            btn.setAttribute('data-tooltip-key', 'toolbar.market');
        });
    }
}

// Инициализация языка при загрузке
function initLanguage(lang) {
    // Если язык передан из C# - используем его, иначе берем из localStorage или ru по умолчанию
    const language = lang || localStorage.getItem('language') || 'ru';
    setLanguage(language);
}
