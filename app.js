// API Configuration - Set backend base URLs dynamically
const backendHost = (window.location.hostname && window.location.hostname !== "localhost" && window.location.hostname !== "127.0.0.1")
  ? window.location.hostname
  : "localhost";

const API_BASE_URL = (window.location.protocol === "file:" || window.location.port === "8000" || window.location.port === "3000")
  ? `http://${backendHost}:5250/api`
  : `${window.location.origin}/api`;

const HUB_URL = (window.location.protocol === "file:" || window.location.port === "8000" || window.location.port === "3000")
  ? `http://${backendHost}:5250/hub/notifications`
  : `${window.location.origin}/hub/notifications`;

// App State
let currentUser = null;
let currentToken = null;
let activeRole = 'Resident'; // 'Resident', 'Security', 'Admin'
let countdownInterval = null;
let signalRConnection = null;

// PWA Service Worker Registration
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('./service-worker.js')
      .then(reg => console.log('Service Worker registered successfully!', reg.scope))
      .catch(err => console.error('Service Worker registration failed:', err));
  });
}

// PWA Install Prompt Handler
let deferredPrompt;
const installBanner = document.getElementById('pwa-install-banner');
const installBtn = document.getElementById('btn-pwa-install');

window.addEventListener('beforeinstallprompt', (e) => {
  e.preventDefault();
  deferredPrompt = e;
  installBanner.style.display = 'flex'; // Show banner
});

installBtn.addEventListener('click', async () => {
  if (!deferredPrompt) return;
  deferredPrompt.prompt();
  const { outcome } = await deferredPrompt.userChoice;
  console.log(`User response to install prompt: ${outcome}`);
  deferredPrompt = null;
  installBanner.style.display = 'none';
});

// ==========================================
// INITIALIZATION & VIEW NAVIGATION
// ==========================================
document.addEventListener('DOMContentLoaded', () => {
  initRoleSelectors();
  initLoginForm();
  initResidentPanel();
  initAdminPanel();
  initMockLpr();
  initAdminComplaintModal();

  // Push Notification Enable Button Click (Banner)
  const btnEnablePush = document.getElementById('btn-enable-push');
  if (btnEnablePush) {
    btnEnablePush.addEventListener('click', async () => {
      await setupPushNotifications();
      if (Notification.permission === 'granted') {
        const pushBanner = document.getElementById('push-notification-banner');
        if (pushBanner) pushBanner.style.display = 'none';
      }
    });
  }

  // Push Permission Modal Events
  const btnModalEnablePush = document.getElementById('btn-modal-enable-push');
  const btnModalClosePush = document.getElementById('btn-modal-close-push');
  const pushModal = document.getElementById('push-permission-modal');

  if (btnModalEnablePush && pushModal) {
    btnModalEnablePush.addEventListener('click', async () => {
      await setupPushNotifications();
      pushModal.style.display = 'none';
    });
  }

  if (btnModalClosePush && pushModal) {
    btnModalClosePush.addEventListener('click', () => {
      sessionStorage.setItem('push_prompted', 'true');
      pushModal.style.display = 'none';
    });
  }
  
  // Check if session or localStorage contains token (Remember Me)
  const savedToken = localStorage.getItem('sitepass_token') || sessionStorage.getItem('sitepass_token');
  const savedUser = localStorage.getItem('sitepass_user') || sessionStorage.getItem('sitepass_user');
  
  if (savedToken && savedUser) {
    currentToken = savedToken;
    currentUser = JSON.parse(savedUser);
    showPanelForRole(currentUser.role);
    startSignalR();
    setupPushNotifications();
  }
});

// Role selector buttons on login screen
function initRoleSelectors() {
  const roleButtons = document.querySelectorAll('.role-btn');
  roleButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      roleButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      activeRole = btn.getAttribute('data-role');
    });
  });
}

function showPanelForRole(role) {
  // Hide all views
  document.querySelectorAll('.view').forEach(view => view.classList.remove('active'));
  document.getElementById('app-header').style.display = 'none'; // Hide landing header inside panel
  
  if (role === 'Resident') {
    document.getElementById('view-resident').classList.add('active');
    loadResidentData();
    
    // Manage push banner display
    const pushBanner = document.getElementById('push-notification-banner');
    if (pushBanner) {
      if ('Notification' in window && Notification.permission !== 'granted') {
        pushBanner.style.display = 'flex';
      } else {
        pushBanner.style.display = 'none';
      }
    }

    // Manage push modal display (prompt immediately on login/auto-login)
    const pushModal = document.getElementById('push-permission-modal');
    if (pushModal) {
      const alreadyPrompted = sessionStorage.getItem('push_prompted');
      if ('Notification' in window && Notification.permission !== 'granted' && !alreadyPrompted) {
        pushModal.style.display = 'flex';
      } else {
        pushModal.style.display = 'none';
      }
    }
  } else if (role === 'Security') {
    document.getElementById('view-security').classList.add('active');
    loadSecurityData();
  } else if (role === 'Admin') {
    document.getElementById('view-admin').classList.add('active');
    loadAdminData();
  }
}

function logout() {
  // Stop countdowns and SignalR
  if (countdownInterval) clearInterval(countdownInterval);
  if (signalRConnection) signalRConnection.stop();
  
  // Clear storage
  localStorage.removeItem('sitepass_token');
  localStorage.removeItem('sitepass_user');
  sessionStorage.removeItem('sitepass_token');
  sessionStorage.removeItem('sitepass_user');
  
  currentUser = null;
  currentToken = null;
  
  // Show login screen
  document.querySelectorAll('.view').forEach(view => view.classList.remove('active'));
  document.getElementById('view-login').classList.add('active');
  document.getElementById('app-header').style.display = 'block';
  
  // Clear forms
  document.getElementById('login-form').reset();
  showToast('🔑 Oturum Kapatıldı', 'Güvenli bir şekilde çıkış yaptınız.', 'info');
}

// ==========================================
// TOAST NOTIFICATIONS UTILITY
// ==========================================
function showToast(title, message, type = 'info') {
  const container = document.getElementById('toast-container');
  const toast = document.createElement('div');
  toast.className = `toast ${type}`;
  
  let emoji = 'ℹ️';
  if (type === 'success') emoji = '✅';
  if (type === 'warning') emoji = '⚠️';
  if (type === 'danger') emoji = '🚨';
  
  toast.innerHTML = `
    <div style="font-size: 1.5rem;">${emoji}</div>
    <div class="toast-content">
      <div class="toast-title">${title}</div>
      <div class="toast-message">${message}</div>
    </div>
  `;
  
  container.appendChild(toast);
  
  // Audio chime alert for elderly notifications
  try {
    const context = new (window.AudioContext || window.webkitAudioContext)();
    const osc = context.createOscillator();
    const gain = context.createGain();
    osc.connect(gain);
    gain.connect(context.destination);
    
    if (type === 'success') {
      osc.frequency.setValueAtTime(587.33, context.currentTime); // D5
      osc.frequency.setValueAtTime(880, context.currentTime + 0.1); // A5
      gain.gain.setValueAtTime(0.1, context.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.01, context.currentTime + 0.3);
      osc.start();
      osc.stop(context.currentTime + 0.3);
    } else if (type === 'danger' || type === 'warning') {
      osc.type = 'sawtooth';
      osc.frequency.setValueAtTime(220, context.currentTime); // A3
      gain.gain.setValueAtTime(0.1, context.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.01, context.currentTime + 0.4);
      osc.start();
      osc.stop(context.currentTime + 0.4);
    }
  } catch (e) {
    // Audio Context might be blocked or unsupported, fail silently
  }

  // Remove toast after 6 seconds
  setTimeout(() => {
    toast.style.animation = 'fadeIn 0.3s ease reverse forwards';
    setTimeout(() => toast.remove(), 300);
  }, 6000);
}

// ==========================================
// AUTHENTICATION & LOGIN FORM
// ==========================================
function initLoginForm() {
  const form = document.getElementById('login-form');
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    
    const phone = document.getElementById('loginPhone').value.trim();
    const password = document.getElementById('loginPassword').value.trim();
    const rememberMe = document.getElementById('rememberMe').checked;
    
    const submitBtn = document.getElementById('btn-login-submit');
    submitBtn.disabled = true;
    submitBtn.innerHTML = 'Giriş Yapılıyor...';
    
    try {
      const response = await fetch(`${API_BASE_URL}/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ phoneNumber: phone, password: password })
      });
      
      const data = await response.json();
      
      if (!response.ok) {
        throw new Error(data.message || 'Giriş başarısız. Telefon veya şifre hatalı.');
      }
      
      // Save details
      currentToken = data.token;
      currentUser = data.user;
      
      if (rememberMe) {
        localStorage.setItem('sitepass_token', currentToken);
        localStorage.setItem('sitepass_user', JSON.stringify(currentUser));
      } else {
        sessionStorage.setItem('sitepass_token', currentToken);
        sessionStorage.setItem('sitepass_user', JSON.stringify(currentUser));
      }
      
      showToast('🎉 Giriş Başarılı', `Hoş geldiniz, Sn. ${currentUser.name}`, 'success');
      showPanelForRole(currentUser.role);
      startSignalR();
      setupPushNotifications();
      
    } catch (err) {
      showToast('❌ Giriş Hatası', err.message, 'danger');
    } finally {
      submitBtn.disabled = false;
      submitBtn.innerHTML = 'Giriş Yap';
    }
  });
}

// ==========================================
// SIGNALR REAL-TIME NOTIFICATIONS
// ==========================================
function startSignalR() {
  if (!currentToken) return;

  signalRConnection = new signalR.HubConnectionBuilder()
    .withUrl(HUB_URL, {
      accessTokenFactory: () => currentToken
    })
    .withAutomaticReconnect()
    .build();

  signalRConnection.on("ReceiveNotification", (notification) => {
    console.log("Real-time notification received: ", notification);
    showToast(notification.title, notification.message, "success");
    
    // Play a premium alert sound using Web Audio API
    try {
      const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
      
      // Beep 1
      const osc1 = audioCtx.createOscillator();
      const gain1 = audioCtx.createGain();
      osc1.connect(gain1);
      gain1.connect(audioCtx.destination);
      osc1.frequency.setValueAtTime(880, audioCtx.currentTime);
      gain1.gain.setValueAtTime(0.1, audioCtx.currentTime);
      gain1.gain.exponentialRampToValueAtTime(0.01, audioCtx.currentTime + 0.15);
      osc1.start();
      osc1.stop(audioCtx.currentTime + 0.15);
      
      // Beep 2
      setTimeout(() => {
        const osc2 = audioCtx.createOscillator();
        const gain2 = audioCtx.createGain();
        osc2.connect(gain2);
        gain2.connect(audioCtx.destination);
        osc2.frequency.setValueAtTime(1200, audioCtx.currentTime);
        gain2.gain.setValueAtTime(0.15, audioCtx.currentTime);
        gain2.gain.exponentialRampToValueAtTime(0.01, audioCtx.currentTime + 0.25);
        osc2.start();
        osc2.stop(audioCtx.currentTime + 0.25);
      }, 80);
    } catch (e) {
      console.warn("Could not play notification sound:", e);
    }

    // Speak the notification title and message using Speech Synthesis
    if ('speechSynthesis' in window) {
      try {
        const utterance = new SpeechSynthesisUtterance("Misafiriniz siteye giriş yapmıştır.");
        utterance.lang = 'tr-TR';
        utterance.rate = 1.0;
        window.speechSynthesis.speak(utterance);
      } catch (e) {
        console.warn("Text-to-Speech failed:", e);
      }
    }

    // Auto refresh active lists depending on current active panel
    if (currentUser && currentUser.role === 'Resident') {
      loadActiveGuestVehicles();
    } else if (currentUser && currentUser.role === 'Security') {
      loadSecurityDeliveries();
    }
  });

  signalRConnection.start()
    .then(() => console.log("Connected to SignalR Notification Hub!"))
    .catch(err => {
      console.error("SignalR connection failed: ", err);
      // Try again in 5 seconds
      setTimeout(startSignalR, 5000);
    });
}

// ==========================================
// RESIDENT PANEL LOGIC (SAKİN)
// ==========================================
function initResidentPanel() {
  // Resident Sub-menu Quick Selector (Guest, Delivery, Complaint)
  const btnMenuGuest = document.getElementById('btn-menu-guest');
  const btnMenuDelivery = document.getElementById('btn-menu-delivery');
  const btnMenuComplaint = document.getElementById('btn-menu-complaint');
  const guestSection = document.getElementById('resident-guest-section');
  const deliverySection = document.getElementById('resident-delivery-section');
  const complaintSection = document.getElementById('resident-complaint-section');

  function selectResidentSubMenu(mode) {
    btnMenuGuest.classList.remove('active');
    btnMenuDelivery.classList.remove('active');
    btnMenuComplaint.classList.remove('active');
    guestSection.style.display = 'none';
    deliverySection.style.display = 'none';
    complaintSection.style.display = 'none';

    if (mode === 'guest') {
      btnMenuGuest.classList.add('active');
      guestSection.style.display = 'block';
    } else if (mode === 'delivery') {
      btnMenuDelivery.classList.add('active');
      deliverySection.style.display = 'block';
    } else if (mode === 'complaint') {
      btnMenuComplaint.classList.add('active');
      complaintSection.style.display = 'block';
    }
  }

  if (btnMenuGuest && btnMenuDelivery && btnMenuComplaint) {
    btnMenuGuest.addEventListener('click', () => selectResidentSubMenu('guest'));
    btnMenuDelivery.addEventListener('click', () => selectResidentSubMenu('delivery'));
    btnMenuComplaint.addEventListener('click', () => selectResidentSubMenu('complaint'));
  }

  // Live Word Counter for Complaints Text Area (300 words limit)
  const complaintText = document.getElementById('complaintText');
  const complaintWordCounter = document.getElementById('complaint-word-counter');
  
  if (complaintText && complaintWordCounter) {
    complaintText.addEventListener('input', () => {
      const text = complaintText.value.trim();
      const words = text ? text.split(/\s+/) : [];
      const wordCount = words.length;
      complaintWordCounter.innerText = `${wordCount} / 300 kelime`;
      
      if (wordCount > 300) {
        complaintWordCounter.style.color = 'var(--danger)';
      } else {
        complaintWordCounter.style.color = 'var(--text-muted)';
      }
    });
  }

  // Resident Complaint Form submission
  const complaintForm = document.getElementById('complaint-form');
  if (complaintForm) {
    complaintForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const text = complaintText.value.trim();
      const words = text ? text.split(/\s+/) : [];
      if (words.length > 300) {
        showToast('⚠️ Limiti Aştınız', 'Şikayet metniniz 300 kelimeden fazla olamaz.', 'warning');
        return;
      }

      const submitBtn = document.getElementById('btn-complaint-submit');
      submitBtn.disabled = true;
      submitBtn.innerHTML = 'Gönderiliyor...';

      try {
        const response = await fetch(`${API_BASE_URL}/complaint`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${currentToken}`
          },
          body: JSON.stringify({ text })
        });

        const data = await response.json();

        if (!response.ok) {
          throw new Error(data.message || 'Şikayet iletilemedi.');
        }

        showToast('✅ Şikayet İletildi', 'Şikayetiniz yöneticiye başarıyla ulaştırıldı.', 'success');
        complaintForm.reset();
        complaintWordCounter.innerText = '0 / 300 kelime';
        loadResidentComplaints();
        selectResidentSubMenu('complaint');

      } catch (err) {
        showToast('❌ Hata', err.message, 'danger');
      } finally {
        submitBtn.disabled = false;
        submitBtn.innerHTML = '⚠️ Şikayeti Gönder';
      }
    });
  }

  const guestForm = document.getElementById('guest-vehicle-form');
  const plateInput = document.getElementById('guestPlate');
  
  // Custom plate normalization on key input (auto capitalize, spaces removal, Turkish character support)
  plateInput.addEventListener('input', (e) => {
    let cursorPosition = e.target.selectionStart;
    let originalLength = e.target.value.length;
    
    // Auto-uppercase Turkish character safe & remove spaces
    let value = e.target.value.toUpperCase().replace(/\s/g, '');
    
    // Replace Turkish lowercase specific chars if any slipped through
    value = value.replace(/ı/g, 'I').replace(/i/g, 'İ');
    
    e.target.value = value;
    
    // Adjust cursor position if spaces were stripped
    let lengthDifference = originalLength - value.length;
    e.target.setSelectionRange(cursorPosition - lengthDifference, cursorPosition - lengthDifference);
  });

  guestForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const plate = plateInput.value.trim();
    
    try {
      const response = await fetch(`${API_BASE_URL}/vehicle/guest`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${currentToken}`
        },
        body: JSON.stringify({ plate })
      });
      
      const data = await response.json();
      
      if (!response.ok) {
        throw new Error(data.message || 'Plaka tanımlama başarısız.');
      }
      
      showToast('🚗 Araç Kaydedildi', `${plate} plakalı misafir aracı tanımlandı. Bariyer izni başladı.`, 'success');
      plateInput.value = '';
      loadActiveGuestVehicles();
      
    } catch (err) {
      showToast('⚠️ Tanımlama Hatası', err.message, 'warning');
    }
  });

  // Expected Delivery Button click
  const deliveryBtn = document.getElementById('btn-expect-delivery');
  deliveryBtn.addEventListener('click', async () => {
    deliveryBtn.disabled = true;
    
    try {
      const response = await fetch(`${API_BASE_URL}/delivery/expected`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${currentToken}`
        },
        body: JSON.stringify({ deliveryType: 'Kargo/Sipariş' })
      });
      
      const data = await response.json();
      
      if (!response.ok) {
        throw new Error(data.message || 'Kayıt başarısız.');
      }
      
      showToast('📦 Talep İletildi', 'Güvenliğe beklenen kargo kaydı düştü. Giriş yaptığında size bildirilecektir.', 'success');
      loadActiveDeliveries();
      
    } catch (err) {
      showToast('⚠️ İstek Hatası', err.message, 'warning');
    } finally {
      // Small cooldown to prevent double taps
      setTimeout(() => {
        deliveryBtn.disabled = false;
      }, 3000);
    }
  });
}

function loadResidentData() {
  // Set header details
  document.getElementById('resident-user-name').innerText = currentUser.name;
  document.getElementById('resident-avatar').innerText = currentUser.name.split(' ').map(n => n[0]).join('').substring(0,2);
  document.getElementById('resident-apartment-details').innerText = `${currentUser.blockNo}/${currentUser.apartmentNo}`;
  
  // Reset selector state to no choice active initially
  const btnMenuGuest = document.getElementById('btn-menu-guest');
  const btnMenuDelivery = document.getElementById('btn-menu-delivery');
  const btnMenuComplaint = document.getElementById('btn-menu-complaint');
  const guestSection = document.getElementById('resident-guest-section');
  const deliverySection = document.getElementById('resident-delivery-section');
  const complaintSection = document.getElementById('resident-complaint-section');
  
  if (btnMenuGuest && btnMenuDelivery && btnMenuComplaint && guestSection && deliverySection && complaintSection) {
    btnMenuGuest.classList.remove('active');
    btnMenuDelivery.classList.remove('active');
    btnMenuComplaint.classList.remove('active');
    guestSection.style.display = 'none';
    deliverySection.style.display = 'none';
    complaintSection.style.display = 'none';
  }

  loadActiveGuestVehicles();
  loadActiveDeliveries();
  loadResidentComplaints();
}

async function loadActiveDeliveries() {
  try {
    const response = await fetch(`${API_BASE_URL}/delivery/my-active?t=${Date.now()}`, {
      headers: {
        'Authorization': `Bearer ${currentToken}`,
        'Cache-Control': 'no-cache',
        'Pragma': 'no-cache'
      }
    });
    
    if (!response.ok) throw new Error('Bekleyen kargolar yüklenemedi.');
    
    const deliveries = await response.json();
    const container = document.getElementById('active-deliveries-list');
    
    if (!container) return;
    
    if (deliveries.length === 0) {
      container.innerHTML = `
        <div class="alert alert-info" id="no-active-deliveries-resident">
          <span class="alert-icon">ℹ️</span>
          <div>Şu an beklediğiniz aktif bir kargo/sipariş bulunmamaktadır.</div>
        </div>
      `;
      return;
    }
    
    container.innerHTML = '';
    deliveries.forEach(delivery => {
      const item = document.createElement('div');
      item.className = 'list-item';
      const createdTime = new Date(delivery.createdDate).toLocaleTimeString('tr-TR', {hour: '2-digit', minute:'2-digit'});
      const createdDateStr = new Date(delivery.createdDate).toLocaleDateString('tr-TR');
      
      item.innerHTML = `
        <div class="list-item-details">
          <div style="font-weight: 600; font-size: 1.05rem;">📦 ${delivery.deliveryType}</div>
          <div style="font-size:0.85rem; color:var(--text-muted); margin-top:4px;">
            Beklenen Tarih: ${createdDateStr} - ${createdTime}
          </div>
        </div>
        <button class="btn btn-danger btn-sm" onclick="cancelDelivery(${delivery.id})" style="padding: 6px 12px; font-size: 0.85rem; min-height: unset; margin: 0;">
          İptal Et
        </button>
      `;
      container.appendChild(item);
    });
  } catch (err) {
    console.error('Kargo yükleme hatası:', err);
  }
}

async function cancelDelivery(id) {
  if (!confirm('Bu kargo beklentisini iptal etmek istediğinize emin misiniz?')) return;
  
  try {
    const response = await fetch(`${API_BASE_URL}/delivery/cancel/${id}`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${currentToken}`
      }
    });
    
    const data = await response.json();
    if (!response.ok) {
      throw new Error(data.message || 'İptal işlemi başarısız.');
    }
    
    showToast('🗑️ İptal Edildi', 'Kargo beklentisi başarıyla iptal edildi.', 'success');
    loadActiveDeliveries();
  } catch (err) {
    showToast('⚠️ Hata', err.message, 'danger');
  }
}
window.cancelDelivery = cancelDelivery;

async function loadActiveGuestVehicles() {
  try {
    const response = await fetch(`${API_BASE_URL}/vehicle/active-guests?t=${Date.now()}`, {
      headers: {
        'Authorization': `Bearer ${currentToken}`,
        'Cache-Control': 'no-cache',
        'Pragma': 'no-cache'
      }
    });
    
    if (!response.ok) throw new Error('Misafir listesi yüklenemedi.');
    
    const vehicles = await response.json();
    const container = document.getElementById('active-guests-list');
    
    if (vehicles.length === 0) {
      container.innerHTML = `
        <div class="alert alert-info" id="no-active-guests">
          <span class="alert-icon">ℹ️</span>
          <div>Şu an aktif tanımlı misafir aracınız bulunmamaktadır.</div>
        </div>
      `;
      if (countdownInterval) clearInterval(countdownInterval);
      return;
    }
    
    // Clear list
    container.innerHTML = '';
    
    vehicles.forEach(vehicle => {
      const item = document.createElement('div');
      item.className = 'list-item';
      if (vehicle.remainingSeconds <= 0) {
        item.classList.add('passive-card');
      }
      item.innerHTML = `
        <div class="list-item-details">
          <div class="plate-badge">${vehicle.plate}</div>
          <div style="font-size:0.9rem; color:var(--text-muted); margin-top:4px;">Giriş: ${new Date(vehicle.createdDate).toLocaleTimeString('tr-TR')}</div>
        </div>
        <div class="countdown-box">
          <div class="countdown-label">Bariyer İzninin Bitmesine</div>
          <div class="countdown-value" id="timer-${vehicle.id}" data-seconds="${vehicle.remainingSeconds}">Hesaplanıyor...</div>
        </div>
        <button class="btn btn-danger btn-sm" onclick="cancelGuestVehicle(${vehicle.id})" style="padding: 6px 12px; font-size: 0.85rem; min-height: unset; margin: 0; margin-left: 16px;">
          İptal Et
        </button>
      `;
      container.appendChild(item);
    });
    
    // Start/Restart ticking
    startCountdownTicker();
    
  } catch (err) {
    console.error(err);
  }
}

function startCountdownTicker() {
  if (countdownInterval) clearInterval(countdownInterval);
  
  const updateTickers = () => {
    const timers = document.querySelectorAll('.countdown-value');
    let activeTimers = 0;
    
    timers.forEach(timer => {
      let secondsLeft = parseInt(timer.getAttribute('data-seconds'));
      
      if (isNaN(secondsLeft)) return;
      
      if (secondsLeft <= 0) {
        timer.innerHTML = '<span class="countdown-expired">Süre Doldu (Pasif)</span>';
        
        // Hide countdown label when expired
        const label = timer.previousElementSibling;
        if (label && label.classList.contains('countdown-label')) {
          label.style.display = 'none';
        }
        
        const card = timer.closest('.list-item');
        if (card) {
          card.classList.add('passive-card');
        }
      } else {
        activeTimers++;
        secondsLeft--;
        timer.setAttribute('data-seconds', secondsLeft);
        
        const hours = Math.floor(secondsLeft / 3600);
        const mins = Math.floor((secondsLeft % 3600) / 60);
        
        // Hide standard label to display single beautiful string
        const label = timer.previousElementSibling;
        if (label && label.classList.contains('countdown-label')) {
          label.style.display = 'none';
        }
        
        // "Bariyer İzninin Bitmesine: XX saat YY dk kaldı" format
        if (hours > 0) {
          timer.innerText = `Bariyer İzninin Bitmesine: ${hours} saat ${mins} dk kaldı`;
        } else {
          timer.innerText = `Bariyer İzninin Bitmesine: ${mins} dk kaldı`;
        }
      }
    });
    
    if (activeTimers === 0) {
      clearInterval(countdownInterval);
    }
  };
  
  updateTickers(); // initial call
  countdownInterval = setInterval(updateTickers, 1000);
}

// ==========================================
// SECURITY PANEL LOGIC (GÜVENLİK)
// ==========================================
function loadSecurityData() {
  document.getElementById('security-user-name').innerText = currentUser.name;
  document.getElementById('security-avatar').innerText = currentUser.name.split(' ').map(n => n[0]).join('').substring(0,2);
  loadSecurityDeliveries();
}

async function loadSecurityDeliveries() {
  try {
    const response = await fetch(`${API_BASE_URL}/delivery/active-deliveries`, {
      headers: { 'Authorization': `Bearer ${currentToken}` }
    });
    
    if (!response.ok) throw new Error('Teslimat listesi yüklenemedi.');
    
    const deliveries = await response.json();
    const container = document.getElementById('security-deliveries-list');
    
    if (deliveries.length === 0) {
      container.innerHTML = `
        <div class="alert alert-info" id="no-active-deliveries">
          <span class="alert-icon">ℹ️</span>
          <div>Şu an beklenen aktif bir teslimat bulunmamaktadır.</div>
        </div>
      `;
      return;
    }
    
    container.innerHTML = '';
    
    deliveries.forEach(delivery => {
      const card = document.createElement('div');
      card.className = 'delivery-card';
      card.innerHTML = `
        <div class="delivery-info-header">
          <span class="delivery-badge">${delivery.deliveryType}</span>
          <span style="font-size:0.9rem; color:var(--text-muted);">Eklenme: ${new Date(delivery.createdDate).toLocaleTimeString('tr-TR')}</span>
        </div>
        
        <div class="resident-details-box">
          <div class="info-row">
            <span class="info-label">Blok/Daire:</span>
            <span class="info-val" style="font-size: 1.25rem; color: var(--primary);">${delivery.blockNo} Blok - Daire ${delivery.apartmentNo}</span>
          </div>
          <div class="info-row">
            <span class="info-label">Sakin:</span>
            <span class="info-val">${delivery.residentName}</span>
          </div>
          <div class="info-row">
            <span class="info-label">Telefon:</span>
            <span class="info-val">
              <a href="tel:${delivery.residentPhone}" class="phone-link">📞 ${delivery.residentPhone}</a>
            </span>
          </div>
        </div>
        
        <button class="btn btn-success btn-approve-delivery" data-id="${delivery.id}">
          <span>ONAYLA (SİTEYE AL)</span>
        </button>
      `;
      container.appendChild(card);
    });
    
    // Bind approve action buttons
    document.querySelectorAll('.btn-approve-delivery').forEach(btn => {
      btn.addEventListener('click', async (e) => {
        const id = btn.getAttribute('data-id');
        btn.disabled = true;
        btn.innerHTML = 'İşleniyor...';
        await approveDelivery(id);
      });
    });
    
  } catch (err) {
    console.error(err);
  }
}

async function approveDelivery(deliveryId) {
  try {
    const response = await fetch(`${API_BASE_URL}/delivery/approve/${deliveryId}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${currentToken}`
      }
    });
    
    if (!response.ok) throw new Error('Onay işlemi gerçekleştirilemedi.');
    
    showToast('📦 Teslimat Onaylandı', 'Daire sakinine bilgi verildi ve kayıt listeden kaldırıldı.', 'success');
    loadSecurityDeliveries();
    
  } catch (err) {
    showToast('❌ Hata', err.message, 'danger');
    loadSecurityDeliveries(); // reload state
  }
}

// ==========================================
// ADMIN PANEL LOGIC (YÖNETİCİ)
// ==========================================
function initAdminPanel() {
  // Tabs toggle
  const tabs = document.querySelectorAll('.tab-btn');
  tabs.forEach(tab => {
    tab.addEventListener('click', () => {
      tabs.forEach(t => t.classList.remove('active'));
      tab.classList.add('active');
      
      document.querySelectorAll('.admin-section').forEach(s => s.classList.remove('active'));
      const target = tab.getAttribute('data-target');
      document.getElementById(target).classList.add('active');

      if (target === 'admin-section-complaints') {
        loadAdminComplaints();
      }
    });
  });

  // User form submission
  const userForm = document.getElementById('admin-user-form');
  userForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    
    const firstName = document.getElementById('userFirstName').value.trim();
    const lastName = document.getElementById('userLastName').value.trim();
    const role = document.getElementById('userRole').value;
    const blockNo = document.getElementById('userBlock').value.trim();
    const apartmentNo = document.getElementById('userApartment').value.trim();
    const phoneNumber = document.getElementById('userPhone').value.trim();
    const password = document.getElementById('userPassword').value.trim();
    
    try {
      const response = await fetch(`${API_BASE_URL}/admin/users`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${currentToken}`
        },
        body: JSON.stringify({ firstName, lastName, role, blockNo, apartmentNo, phoneNumber, password })
      });
      
      const data = await response.json();
      
      if (!response.ok) throw new Error(data.message || 'Kullanıcı eklenemedi.');
      
      showToast('👤 Kullanıcı Eklendi', `${firstName} ${lastName} sisteme tanımlandı.`, 'success');
      userForm.reset();
      loadAdminUsers();
      
    } catch (err) {
      showToast('❌ Hata', err.message, 'danger');
    }
  });

  // Vehicle form submission
  const vehicleForm = document.getElementById('admin-vehicle-form');
  const permPlateInput = document.getElementById('permPlate');
  
  // Normalize permanent plate too
  permPlateInput.addEventListener('input', (e) => {
    let value = e.target.value.toUpperCase().replace(/\s/g, '').replace(/ı/g, 'I').replace(/i/g, 'İ');
    e.target.value = value;
  });

  vehicleForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const residentId = document.getElementById('permVehicleResident').value;
    const plate = permPlateInput.value.trim();
    
    try {
      const response = await fetch(`${API_BASE_URL}/admin/vehicles`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${currentToken}`
        },
        body: JSON.stringify({ plate, residentId: parseInt(residentId) })
      });
      
      const data = await response.json();
      
      if (!response.ok) throw new Error(data.message || 'Plaka eklenemedi.');
      
      showToast('🚗 Kalıcı Plaka Eklendi', `${plate} sakin için kaydedildi.`, 'success');
      vehicleForm.reset();
      loadAdminVehicles();
      
    } catch (err) {
      showToast('❌ Hata', err.message, 'danger');
    }
  });
}

function loadAdminData() {
  document.getElementById('admin-user-name').innerText = currentUser.name;
  document.getElementById('admin-avatar').innerText = currentUser.name.split(' ').map(n => n[0]).join('').substring(0,2);
  
  loadAdminUsers();
  loadAdminVehicles();
  loadAdminComplaints();
}

async function loadAdminUsers() {
  try {
    const response = await fetch(`${API_BASE_URL}/admin/users`, {
      headers: { 'Authorization': `Bearer ${currentToken}` }
    });
    
    if (!response.ok) throw new Error('Kullanıcılar yüklenemedi.');
    
    const users = await response.json();
    const tableBody = document.getElementById('admin-users-table-body');
    const residentSelect = document.getElementById('permVehicleResident');
    
    tableBody.innerHTML = '';
    residentSelect.innerHTML = '<option value="">Daire Sakini Seçin...</option>';
    
    users.forEach(user => {
      // Row
      const row = document.createElement('tr');
      const location = user.blockNo ? `${user.blockNo}/${user.apartmentNo}` : '-';
      row.innerHTML = `
        <td><strong>${user.firstName} ${user.lastName}</strong></td>
        <td><span class="delivery-badge" style="background-color: ${user.role === 'Admin' ? '#ef4444' : user.role === 'Security' ? '#3b82f6' : '#10b981'}">${user.role}</span></td>
        <td>${location}</td>
        <td>${user.phoneNumber}</td>
        <td>
          <button class="btn-icon-only btn-delete-user" data-id="${user.id}">🗑️</button>
        </td>
      `;
      tableBody.appendChild(row);
      
      // Select dropdown option if Resident
      if (user.role === 'Resident') {
        const opt = document.createElement('option');
        opt.value = user.id;
        opt.innerText = `${user.firstName} ${user.lastName} (${location})`;
        residentSelect.appendChild(opt);
      }
    });
    
    // Bind delete actions
    document.querySelectorAll('.btn-delete-user').forEach(btn => {
      btn.addEventListener('click', async () => {
        const id = btn.getAttribute('data-id');
        if (confirm('Bu kullanıcıyı sistemden silmek istediğinize emin misiniz?')) {
          await deleteUser(id);
        }
      });
    });
    
  } catch (err) {
    console.error(err);
  }
}

async function deleteUser(userId) {
  try {
    const response = await fetch(`${API_BASE_URL}/admin/users/${userId}`, {
      method: 'DELETE',
      headers: { 'Authorization': `Bearer ${currentToken}` }
    });
    
    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.message || 'Kullanıcı silinemedi.');
    }
    
    showToast('🗑️ Kullanıcı Silindi', 'Kullanıcı kaydı veritabanından kaldırıldı.', 'success');
    loadAdminUsers();
    
  } catch (err) {
    showToast('❌ Hata', err.message, 'danger');
  }
}

async function loadAdminVehicles() {
  try {
    const response = await fetch(`${API_BASE_URL}/admin/vehicles`, {
      headers: { 'Authorization': `Bearer ${currentToken}` }
    });
    
    if (!response.ok) throw new Error('Plakalar yüklenemedi.');
    
    const vehicles = await response.json();
    const tableBody = document.getElementById('admin-vehicles-table-body');
    
    tableBody.innerHTML = '';
    
    vehicles.forEach(vehicle => {
      const row = document.createElement('tr');
      row.innerHTML = `
        <td><div class="plate-badge">${vehicle.plate}</div></td>
        <td>${vehicle.residentName}</td>
        <td>Daire: ${vehicle.blockNo}/${vehicle.apartmentNo}</td>
        <td><span class="delivery-badge" style="background-color: ${vehicle.isGuest ? '#f59e0b' : '#10b981'}">${vehicle.isGuest ? 'Misafir' : 'Sabit'}</span></td>
        <td>
          <button class="btn-icon-only btn-delete-vehicle" data-id="${vehicle.id}">🗑️</button>
        </td>
      `;
      tableBody.appendChild(row);
    });
    
    // Bind delete actions
    document.querySelectorAll('.btn-delete-vehicle').forEach(btn => {
      btn.addEventListener('click', async () => {
        const id = btn.getAttribute('data-id');
        if (confirm('Bu plakayı kaldırmak istediğinize emin misiniz?')) {
          await deleteVehicle(id);
        }
      });
    });
    
  } catch (err) {
    console.error(err);
  }
}

async function deleteVehicle(vehicleId) {
  try {
    const response = await fetch(`${API_BASE_URL}/admin/vehicles/${vehicleId}`, {
      method: 'DELETE',
      headers: { 'Authorization': `Bearer ${currentToken}` }
    });
    
    if (!response.ok) throw new Error('Plaka sistemden kaldırılamadı.');
    
    showToast('🗑️ Plaka Silindi', 'Araç kaydı silindi, bariyer yetkisi iptal edildi.', 'success');
    loadAdminVehicles();
    
  } catch (err) {
    showToast('❌ Hata', err.message, 'danger');
  }
}

// ==========================================
// MOCK LPR SIMULATOR CONTROL (SADECE TEST)
// ==========================================
function initMockLpr() {
  const mockPlateInput = document.getElementById('mockLprPlate');
  const mockSubmitBtn = document.getElementById('btn-mock-lpr-submit');
  
  // Webcam elements
  const btnToggleCamera = document.getElementById('btn-toggle-camera');
  const cameraContainer = document.getElementById('camera-container');
  const cameraFeed = document.getElementById('camera-feed');
  const cameraCanvas = document.getElementById('camera-canvas');
  const btnCameraCapture = document.getElementById('btn-camera-capture');
  const ocrStatus = document.getElementById('ocr-status');

  // Camera File Input Fallback for Mobile HTTP
  const cameraFileInput = document.getElementById('camera-file-input');
  const btnUploadPhoto = document.getElementById('btn-upload-photo');

  let stream = null;
  
  mockPlateInput.addEventListener('input', (e) => {
    e.target.value = e.target.value.toUpperCase().replace(/\s/g, '').replace(/ı/g, 'I').replace(/i/g, 'İ');
  });

  // Toggle Camera Feed
  btnToggleCamera.addEventListener('click', async () => {
    if (stream) {
      stopCamera();
    } else {
      await startCamera();
    }
  });

  async function startCamera() {
    try {
      ocrStatus.innerText = "Kamera başlatılıyor...";
      
      if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        throw new Error("Tarayıcınız veya bağlantı protokolünüz kamera erişimini engelliyor. Lütfen sayfaya 'http://localhost:8000' adresiyle (IP adresi yerine) bağlandığınızdan emin olun.");
      }
      
      stream = await navigator.mediaDevices.getUserMedia({ 
        video: { facingMode: 'environment', width: { ideal: 640 }, height: { ideal: 480 } }, 
        audio: false 
      });
      cameraFeed.srcObject = stream;
      cameraContainer.style.display = 'flex';
      btnToggleCamera.innerText = '🛑 Kamerayı Kapat';
      btnToggleCamera.className = 'btn btn-danger';
      ocrStatus.innerText = "Kamera hazır. Plakayı kadraja ortalayıp butona basın.";
    } catch (err) {
      console.error(err);
      ocrStatus.innerText = "⚠️ " + err.message;
      showToast('❌ Kamera Hatası', err.message, 'danger');
    }
  }

  function stopCamera() {
    if (stream) {
      stream.getTracks().forEach(track => track.stop());
      stream = null;
    }
    cameraFeed.srcObject = null;
    cameraContainer.style.display = 'none';
    btnToggleCamera.innerText = '🎥 Web Kamerayı Başlat';
    btnToggleCamera.className = 'btn btn-secondary';
    ocrStatus.innerText = "";
  }

  // Smart character correction heuristic specifically for Turkish plates
  function normalizeAndCorrectPlate(text) {
    let clean = text.toUpperCase().replace(/[^A-Z0-9]/g, '').replace(/İ/g, 'I');
    if (clean.length < 5 || clean.length > 10) return clean;

    const letterToDigit = {
      'O': '0', 'Q': '0', 'D': '0',
      'I': '1', 'L': '1', 'T': '1', 'J': '1',
      'Z': '2', 'B': '8', 'S': '5', 'G': '5', 'A': '4'
    };
    
    const digitToLetter = {
      '0': 'O', '1': 'I', '2': 'Z', '5': 'S', '8': 'B', '4': 'A'
    };

    function isLetterLike(char) {
      if (!char) return false;
      return /[A-Z]/.test(char) || ['0', '1', '2', '5', '8', '4'].includes(char);
    }

    // Partitioning: Turkish plates always start with 2-digit city code (index 0,1)
    let part1 = clean.substring(0, 2);
    let letterLen = 1;

    // Detect middle letter section length (index 2 onwards)
    if (isLetterLike(clean[2])) {
      if (isLetterLike(clean[3])) {
        if (isLetterLike(clean[4]) && clean.length > 6) {
          letterLen = 3;
        } else {
          letterLen = 2;
        }
      } else {
        letterLen = 1;
      }
    }

    let part2 = clean.substring(2, 2 + letterLen);
    let part3 = clean.substring(2 + letterLen);

    // Correct Part 1: First 2 chars must be digits
    let correctedPart1 = "";
    for (let char of part1) {
      correctedPart1 += letterToDigit[char] || char;
    }

    // Correct Part 2: Middle chars must be letters
    let correctedPart2 = "";
    for (let char of part2) {
      correctedPart2 += digitToLetter[char] || char;
    }

    // Correct Part 3: Last 2-4 chars must be digits
    let correctedPart3 = "";
    for (let char of part3) {
      correctedPart3 += letterToDigit[char] || char;
    }

    const corrected = correctedPart1 + correctedPart2 + correctedPart3;
    console.log(`OCR Correction Helper: ${clean} -> ${corrected}`);
    return corrected;
  }

  // Shared OCR response handler
  function processOcrResult(result) {
    const rawText = result.data.text || "";
    const rawTextTrimmed = rawText.trim().replace(/\n/g, ' ');
    console.log("OCR Raw Output:", rawTextTrimmed);

    // Clean raw text to uppercase and keep letters, digits, and spaces
    let cleanText = rawText.toUpperCase()
      .replace(/[^A-Z0-9\s]/g, '')
      .replace(/I/g, 'I').replace(/İ/g, 'I');

    // Split into words
    const words = cleanText.split(/\s+/).map(w => w.trim()).filter(w => w.length > 0);
    console.log("Processed words:", words);

    let bestCandidate = "";

    // 1. Look for a single word that contains a plate (e.g. "34ABC123" or "34AB876")
    for (let i = 0; i < words.length; i++) {
      let w = words[i];
      if (w.length >= 5 && w.length <= 9) {
        const hasLetter = /[A-Z]/.test(w);
        const hasDigit = /[0-9]/.test(w);
        if (hasLetter && hasDigit) {
          bestCandidate = normalizeAndCorrectPlate(w);
          break;
        }
      }
    }

    // 2. If not found, look for 3 consecutive words (e.g. ["34", "ABC", "123"])
    if (!bestCandidate) {
      for (let i = 0; i < words.length - 2; i++) {
        let w1 = words[i];
        let w2 = words[i+1];
        let w3 = words[i+2];
        
        if (w1.length === 2 && w2.length >= 1 && w2.length <= 3 && w3.length >= 2 && w3.length <= 4) {
          const combined = w1 + w2 + w3;
          const hasLetter = /[A-Z]/.test(combined);
          const hasDigit = /[0-9]/.test(combined);
          if (hasLetter && hasDigit) {
            bestCandidate = normalizeAndCorrectPlate(combined);
            break;
          }
        }
      }
    }

    // 3. If still not found, collapse everything and search using regex
    if (!bestCandidate) {
      const collapsed = cleanText.replace(/\s+/g, '');
      // Search for Turkish plate pattern anywhere in collapsed string
      const plateRegex = /([0-9A-Z]{2}[A-Z0-9]{1,3}[0-9A-Z]{2,4})/;
      const match = collapsed.match(plateRegex);
      if (match && match[1]) {
        bestCandidate = normalizeAndCorrectPlate(match[1]);
      }
    }

    if (bestCandidate && bestCandidate.length >= 5) {
      mockPlateInput.value = bestCandidate;
      ocrStatus.innerHTML = `🟢 Plaka Okundu: <span style="color:var(--success); font-size:1.1rem; font-weight:800;">${bestCandidate}</span>`;
      showToast('✅ Plaka Okundu', `Kameradan okunan plaka: ${bestCandidate}`, 'success');
      
      // Auto submit after 1.5s
      setTimeout(() => {
        mockSubmitBtn.click();
        stopCamera();
      }, 1500);
    } else {
      // Fallback: put whatever it read to help them edit
      const rawCleaned = cleanText.replace(/\s+/g, '');
      const correctedRaw = normalizeAndCorrectPlate(rawCleaned);
      
      if (correctedRaw.length > 2) {
        mockPlateInput.value = correctedRaw;
        ocrStatus.innerHTML = `🟡 Emin Değiliz (Lütfen Düzenleyin): <span style="color:var(--warning); font-size:1.1rem; font-weight:800;">${correctedRaw}</span>`;
        showToast('⚠️ Plaka Net Okunamadı', `Okunan ham metin: ${correctedRaw}`, 'warning');
      } else {
        ocrStatus.innerHTML = `❌ Plaka okunamadı. <br><span style="color:var(--text-muted); font-size:0.8rem;">Ham Okunan: "${rawTextTrimmed || 'Boş'}"</span>`;
        showToast('❌ Okuma Başarısız', 'Plaka okunamadı. Lütfen ışığı ayarlayıp tekrar deneyin.', 'danger');
      }
    }
  }

  // Camera capture button event
  btnCameraCapture.addEventListener('click', async () => {
    if (!stream) return;

    btnCameraCapture.disabled = true;
    btnCameraCapture.innerText = '⚡ Analiz Ediliyor...';
    ocrStatus.innerText = "🔍 Görüntü yakalandı, plaka taranıyor...";
    
    // Show canvas snapshot preview
    cameraCanvas.style.display = 'block';

    try {
      const videoW = cameraFeed.videoWidth;
      const videoH = cameraFeed.videoHeight;
      
      // Capture the full high-res frame from video to prevent cropping mismatch
      cameraCanvas.width = videoW;
      cameraCanvas.height = videoH;
      
      const ctx = cameraCanvas.getContext('2d');
      ctx.drawImage(cameraFeed, 0, 0, videoW, videoH);

      // Run Tesseract OCR using the high-compatibility recognize method
      const result = await Tesseract.recognize(cameraCanvas, 'eng', {
        logger: m => {
          if (m.status === 'recognizing text') {
            ocrStatus.innerText = `🔍 Plaka Okunuyor: %${Math.round(m.progress * 100)}`;
          }
        }
      });
      processOcrResult(result);
    } catch (err) {
      console.error(err);
      ocrStatus.innerText = "❌ Hata: Plaka okuma motoru çalıştırılamadı.";
      showToast('❌ Hata', 'OCR işlemi esnasında bir hata oluştu.', 'danger');
    } finally {
      btnCameraCapture.disabled = false;
      btnCameraCapture.innerText = '📸 Fotoğraf Çek ve Plaka Tara';
      
      // Hide preview canvas after 6 seconds
      setTimeout(() => {
        cameraCanvas.style.display = 'none';
      }, 6000);
    }
  });

  // Alternative Mobile Image Capture File Upload Handlers
  if (btnUploadPhoto && cameraFileInput) {
    btnUploadPhoto.addEventListener('click', () => {
      cameraFileInput.click();
    });

    cameraFileInput.addEventListener('change', async (e) => {
      const file = e.target.files[0];
      if (!file) return;

      ocrStatus.innerText = "🔍 Görsel yükleniyor...";
      const reader = new FileReader();
      
      reader.onload = async (event) => {
        const img = new Image();
        img.onload = async () => {
          // Draw image to canvas
          cameraCanvas.style.display = 'block';
          cameraCanvas.width = img.width;
          cameraCanvas.height = img.height;
          const ctx = cameraCanvas.getContext('2d');
          ctx.drawImage(img, 0, 0, img.width, img.height);

          ocrStatus.innerText = "🔍 Görsel analiz ediliyor, plaka taranıyor...";
          try {
            // Run Tesseract OCR on the loaded image canvas
            const result = await Tesseract.recognize(cameraCanvas, 'eng', {
              logger: m => {
                if (m.status === 'recognizing text') {
                  ocrStatus.innerText = `🔍 Plaka Okunuyor: %${Math.round(m.progress * 100)}`;
                }
              }
            });
            processOcrResult(result);
          } catch (err) {
            console.error(err);
            ocrStatus.innerText = "❌ Hata: Görselden plaka okunamadı.";
            showToast('❌ Hata', 'OCR işlemi esnasında bir hata oluştu.', 'danger');
          } finally {
            // Hide preview canvas after 6 seconds
            setTimeout(() => {
              cameraCanvas.style.display = 'none';
            }, 6000);
          }
        };
        img.src = event.target.result;
      };
      reader.readAsDataURL(file);
    });
  }

  // Submission simulator
  mockSubmitBtn.addEventListener('click', async () => {
    const plate = mockPlateInput.value.trim();
    if (!plate) {
      showToast('⚠️ Eksik Bilgi', 'Lütfen test için bir plaka girin.', 'warning');
      return;
    }

    mockSubmitBtn.disabled = true;
    mockSubmitBtn.innerHTML = 'Okunuyor...';

    try {
      const response = await fetch(`${API_BASE_URL}/test/kamera-oku`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ plate })
      });

      const data = await response.json();

      if (!response.ok || !data.success) {
        throw new Error(data.message || 'Plaka okunamadı veya aktif yetki bulunamadı.');
      }

      showToast('🟢 LPR Simülasyonu Başarılı', `${data.message}`, 'success');
      mockPlateInput.value = '';

      // Auto refresh list locally immediately
      if (currentUser && currentUser.role === 'Resident') {
        loadActiveGuestVehicles();
      }

    } catch (err) {
      showToast('🔴 LPR Simülasyon Hatası', err.message, 'danger');
    } finally {
      mockSubmitBtn.disabled = false;
      mockSubmitBtn.innerHTML = 'Plakayı Kameradan Oku';
    }
  });
}

async function cancelGuestVehicle(id) {
  if (!confirm('Bu misafir aracın geçiş iznini iptal etmek istediğinize emin misiniz?')) return;
  
  try {
    const response = await fetch(`${API_BASE_URL}/vehicle/cancel/${id}`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${currentToken}`
      }
    });
    
    const data = await response.json();
    if (!response.ok) {
      throw new Error(data.message || 'İptal işlemi başarısız.');
    }
    
    showToast('🗑️ İptal Edildi', 'Misafir araç izni başarıyla iptal edildi.', 'success');
    loadActiveGuestVehicles();
  } catch (err) {
    showToast('⚠️ Hata', err.message, 'danger');
  }
}
window.cancelGuestVehicle = cancelGuestVehicle;

// ==========================================
// PWA NATIVE WEB PUSH NOTIFICATION SYSTEM
// ==========================================
function urlB64ToUint8Array(base64String) {
  const padding = '='.repeat((4 - base64String.length % 4) % 4);
  const base64 = (base64String + padding)
    .replace(/\-/g, '+')
    .replace(/_/g, '/');

  const rawData = window.atob(base64);
  const outputArray = new Uint8Array(rawData.length);

  for (let i = 0; i < rawData.length; ++i) {
    outputArray[i] = rawData.charCodeAt(i);
  }
  return outputArray;
}

async function setupPushNotifications() {
  if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
    console.log("PWA Web Push is not supported by this browser.");
    return;
  }

  try {
    // 1. Request notification permission from user
    const permission = await Notification.requestPermission();
    if (permission !== 'granted') {
      console.log("Notification permission was denied.");
      return;
    }

    // 2. Fetch VAPID public key from backend
    const keyRes = await fetch(`${API_BASE_URL}/auth/vapid-public-key`);
    const keyData = await keyRes.json();
    const publicVapidKey = keyData.publicKey;

    // 3. Register push subscription
    const registration = await navigator.serviceWorker.ready;
    let subscription = await registration.pushManager.getSubscription();
    
    if (!subscription) {
      subscription = await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: urlB64ToUint8Array(publicVapidKey)
      });
    }

    // Convert raw keys to Base64 strings for .NET WebPush compatibility
    const p256dh = btoa(String.fromCharCode.apply(null, new Uint8Array(subscription.getKey('p256dh'))));
    const auth = btoa(String.fromCharCode.apply(null, new Uint8Array(subscription.getKey('auth'))));

    // 4. Save subscription on the backend
    await fetch(`${API_BASE_URL}/auth/subscribe-push`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${currentToken}`
      },
      body: JSON.stringify({
        endpoint: subscription.endpoint,
        p256dh: p256dh,
        auth: auth
      })
    });

    console.log("PWA Web Push notification registered successfully!");
  } catch (err) {
    console.warn("Could not register Web Push subscription:", err);
  }
}
window.setupPushNotifications = setupPushNotifications;

// Load all complaints for the admin
// Load all complaints for the admin
async function loadAdminComplaints() {
  try {
    const response = await fetch(`${API_BASE_URL}/complaint/admin-list`, {
      headers: { 'Authorization': `Bearer ${currentToken}` }
    });
    
    if (!response.ok) throw new Error('Şikayet listesi yüklenemedi.');
    
    const complaints = await response.json();
    const tableBody = document.getElementById('admin-complaints-table-body');
    
    if (!tableBody) return;
    tableBody.innerHTML = '';
    
    if (complaints.length === 0) {
      tableBody.innerHTML = `
        <tr>
          <td colspan="4" style="text-align: center; color: var(--text-muted); padding: 20px;">
            Henüz bildirilmiş bir şikayet bulunmamaktadır.
          </td>
        </tr>
      `;
      return;
    }
    
    complaints.forEach(c => {
      const row = document.createElement('tr');
      const dateStr = new Date(c.createdDate).toLocaleDateString('tr-TR', {
        day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit'
      });
      const location = `${c.blockNo} Blok - Daire ${c.apartmentNo}`;
      const isReplied = c.replyText ? '<span class="delivery-badge" style="background-color: var(--success); font-size: 0.75rem;">Cevaplandı</span>' : '<span class="delivery-badge" style="background-color: var(--warning); font-size: 0.75rem;">Bekliyor</span>';
      
      row.innerHTML = `
        <td>${dateStr}</td>
        <td><strong>${location}</strong></td>
        <td>${c.residentName} ${isReplied}</td>
        <td>
          <button class="btn btn-primary btn-sm btn-view-complaint" data-id="${c.id}" data-text="${encodeURIComponent(c.text)}" data-sender="${escapeHtml(c.residentName)} (${location})" data-date="${dateStr}" data-reply="${encodeURIComponent(c.replyText || '')}" data-replydate="${c.replyDate ? new Date(c.replyDate).toLocaleDateString('tr-TR', {day:'2-digit', month:'2-digit', year:'numeric', hour:'2-digit', minute:'2-digit'}) : ''}" style="margin: 0; min-height: unset; padding: 6px 12px; font-size: 0.85rem;">
            Şikayeti Gör
          </button>
        </td>
      `;
      tableBody.appendChild(row);
    });
    
  } catch (err) {
    console.error(err);
  }
}

function initAdminComplaintModal() {
  const modal = document.getElementById('admin-complaint-modal');
  const btnClose = document.getElementById('btn-close-complaint-modal');
  const btnCancel = document.getElementById('btn-cancel-reply-modal');
  const form = document.getElementById('admin-reply-form');
  const replyText = document.getElementById('replyText');
  const complaintIdInput = document.getElementById('reply-complaint-id');
  const tableBody = document.getElementById('admin-complaints-table-body');
  
  if (!modal) return;

  // Use event delegation on tableBody to catch clicks on "Şikayeti Gör"
  if (tableBody) {
    tableBody.addEventListener('click', (e) => {
      const btn = e.target.closest('.btn-view-complaint');
      if (!btn) return;

      const id = btn.getAttribute('data-id');
      const text = decodeURIComponent(btn.getAttribute('data-text') || '');
      const sender = btn.getAttribute('data-sender') || '';
      const date = btn.getAttribute('data-date') || '';
      const reply = decodeURIComponent(btn.getAttribute('data-reply') || '');
      const replydate = btn.getAttribute('data-replydate') || '';

      // Fill modal elements
      document.getElementById('modal-complaint-text').innerText = text;
      document.getElementById('modal-complaint-sender').innerText = sender;
      document.getElementById('modal-complaint-date').innerText = date;
      complaintIdInput.value = id;
      replyText.value = '';

      // Handle existing reply box
      const replyBox = document.getElementById('modal-existing-reply-box');
      if (reply) {
        document.getElementById('modal-existing-reply-text').innerText = reply;
        document.getElementById('modal-existing-reply-date').innerText = replydate;
        replyBox.style.display = 'block';
      } else {
        replyBox.style.display = 'none';
      }

      // Show modal
      modal.style.display = 'flex';
    });
  }

  // Close actions
  const closeModal = () => {
    modal.style.display = 'none';
  };

  if (btnClose) btnClose.addEventListener('click', closeModal);
  if (btnCancel) btnCancel.addEventListener('click', closeModal);

  // Form submission for reply
  if (form) {
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      const id = complaintIdInput.value;
      const text = replyText.value.trim();

      const submitBtn = document.getElementById('btn-submit-reply');
      submitBtn.disabled = true;
      submitBtn.innerHTML = 'Gönderiliyor...';

      try {
        const response = await fetch(`${API_BASE_URL}/complaint/reply/${id}`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${currentToken}`
          },
          body: JSON.stringify({ text })
        });

        const data = await response.json();

        if (!response.ok) {
          throw new Error(data.message || 'Cevap gönderilemedi.');
        }

        showToast('✅ Cevap İletildi', 'Cevabınız sakine başarıyla ulaştırıldı.', 'success');
        closeModal();
        loadAdminComplaints(); // refresh table

      } catch (err) {
        showToast('❌ Hata', err.message, 'danger');
      } finally {
        submitBtn.disabled = false;
        submitBtn.innerHTML = 'Cevap Gönder';
      }
    });
  }
}

// Load Resident's own complaints
async function loadResidentComplaints() {
  try {
    const response = await fetch(`${API_BASE_URL}/complaint/my-list`, {
      headers: { 'Authorization': `Bearer ${currentToken}` }
    });

    if (!response.ok) throw new Error('Şikayetleriniz yüklenemedi.');

    const complaints = await response.json();
    const container = document.getElementById('resident-complaints-list');

    if (!container) return;
    container.innerHTML = '';

    if (complaints.length === 0) {
      container.innerHTML = `
        <div class="alert alert-info">
          <span class="alert-icon">ℹ️</span>
          <div>Henüz iletilen bir şikayetiniz bulunmamaktadır.</div>
        </div>
      `;
      return;
    }

    complaints.forEach(c => {
      const card = document.createElement('div');
      card.className = 'list-item';
      card.style.flexDirection = 'column';
      card.style.alignItems = 'stretch';
      card.style.gap = '12px';
      card.style.padding = '16px';
      
      const dateStr = new Date(c.createdDate).toLocaleDateString('tr-TR', {
        day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit'
      });

      let replyHtml = '';
      if (c.replyText) {
        const replyDateStr = new Date(c.replyDate).toLocaleDateString('tr-TR', {
          day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit'
        });
        replyHtml = `
          <div style="background-color: rgba(16, 185, 129, 0.08); border: 1px solid var(--success); border-radius: var(--border-radius-md); padding: 12px; margin-top: 8px;">
            <div style="font-weight: 700; color: var(--success); font-size: 0.85rem; margin-bottom: 4px;">💬 Yönetici Yanıtı (${replyDateStr}):</div>
            <div style="font-size: 0.9rem; color: var(--text-main); line-height: 1.4; white-space: pre-wrap; word-break: break-word;">${escapeHtml(c.replyText)}</div>
          </div>
        `;
      } else {
        replyHtml = `
          <div style="font-size: 0.8rem; color: var(--warning); font-style: italic; margin-top: 4px;">
            ⏳ Henüz yanıtlanmadı (Beklemede)
          </div>
        `;
      }

      card.innerHTML = `
        <div style="display: flex; justify-content: space-between; align-items: flex-start; gap: 12px;">
          <div style="flex: 1;">
            <div style="font-size: 0.75rem; color: var(--text-muted); margin-bottom: 4px;">Başvuru Tarihi: ${dateStr}</div>
            <div style="font-size: 0.95rem; font-weight: 500; color: var(--text-main); white-space: pre-wrap; word-break: break-word; line-height: 1.4;">${escapeHtml(c.text)}</div>
          </div>
          <button class="btn btn-danger btn-sm btn-withdraw-complaint" data-id="${c.id}" style="margin: 0; padding: 6px 12px; font-size: 0.8rem; min-height: unset; flex-shrink: 0;">
            Geri Çek
          </button>
        </div>
        ${replyHtml}
      `;
      container.appendChild(card);
    });

    // Bind withdraw actions
    container.querySelectorAll('.btn-withdraw-complaint').forEach(btn => {
      btn.addEventListener('click', async () => {
        const id = btn.getAttribute('data-id');
        if (confirm('Bu şikayet başvurusunu tamamen iptal etmek ve geri çekmek istediğinize emin misiniz?')) {
          await withdrawComplaint(id);
        }
      });
    });

  } catch (err) {
    console.error(err);
  }
}

async function withdrawComplaint(id) {
  try {
    const response = await fetch(`${API_BASE_URL}/complaint/${id}`, {
      method: 'DELETE',
      headers: {
        'Authorization': `Bearer ${currentToken}`
      }
    });

    const data = await response.json();
    if (!response.ok) {
      throw new Error(data.message || 'Şikayet geri çekilemedi.');
    }

    showToast('🗑️ Şikayet Geri Çekildi', 'Şikayet başvurunuz başarıyla silindi.', 'success');
    loadResidentComplaints();

  } catch (err) {
    showToast('❌ Hata', err.message, 'danger');
  }
}

function escapeHtml(text) {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}
