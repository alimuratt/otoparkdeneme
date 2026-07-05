// API Configuration - Set backend base URLs dynamically
const BACKEND_PORT = "5250";
const BACKEND_HOST = window.location.hostname;
const API_BASE_URL = `http://${BACKEND_HOST}:${BACKEND_PORT}/api`;
const HUB_URL = `http://${BACKEND_HOST}:${BACKEND_PORT}/hub/notifications`;

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
  
  // Check if session or localStorage contains token (Remember Me)
  const savedToken = localStorage.getItem('sitepass_token') || sessionStorage.getItem('sitepass_token');
  const savedUser = localStorage.getItem('sitepass_user') || sessionStorage.getItem('sitepass_user');
  
  if (savedToken && savedUser) {
    currentToken = savedToken;
    currentUser = JSON.parse(savedUser);
    showPanelForRole(currentUser.role);
    startSignalR();
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
  
  loadActiveGuestVehicles();
  loadActiveDeliveries();
}

async function loadActiveDeliveries() {
  try {
    const response = await fetch(`${API_BASE_URL}/delivery/my-active`, {
      headers: { 'Authorization': `Bearer ${currentToken}` }
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
    const response = await fetch(`${API_BASE_URL}/vehicle/active-guests`, {
      headers: { 'Authorization': `Bearer ${currentToken}` }
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
        timer.parentElement.previousElementSibling.querySelector('.plate-badge').style.opacity = '0.5';
      } else {
        activeTimers++;
        secondsLeft--;
        timer.setAttribute('data-seconds', secondsLeft);
        
        const hrs = Math.floor(secondsLeft / 3600);
        const mins = Math.floor((secondsLeft % 3600) / 60);
        const secs = secondsLeft % 60;
        
        // Large & clean label for elderly: "XX saat YY dakika"
        timer.innerText = `${hrs} saat ${mins} dakika ${secs} sn`;
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
      document.getElementById(tab.getAttribute('data-target')).classList.add('active');
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
  
  mockPlateInput.addEventListener('input', (e) => {
    e.target.value = e.target.value.toUpperCase().replace(/\s/g, '').replace(/ı/g, 'I').replace(/i/g, 'İ');
  });

  mockSubmitBtn.addEventListener('click', async () => {
    const plate = mockPlateInput.value.trim();
    if (!plate) {
      showToast('⚠️ Eksik Bilgi', 'Lütfen test için bir plaka girin.', 'warning');
      return;
    }

    mockSubmitBtn.disabled = true;
    mockSubmitBtn.innerHTML = 'Okunuyor...';

    try {
      const response = await fetch(`${API_BASE_URL}/vehicle/mock-lpr`, {
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
