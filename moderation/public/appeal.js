(() => {
 const canonical='https://plutoniumclient.vercel.app/appeal';
 if(location.hostname!=='plutoniumclient.vercel.app') { location.replace(canonical); return; }
 const form=document.getElementById('appeal'), result=document.getElementById('result'), button=document.getElementById('submit');
 const status=document.getElementById('verification-status'), retry=document.getElementById('retry');
 let captcha='', widget, busy=false;
 const message=(text,state='error')=>{result.textContent=text;result.dataset.state=state;};
 const verification=(text,again=false)=>{status.textContent=text;retry.hidden=!again;};
 document.getElementById('explanation').addEventListener('input',e=>document.getElementById('counter').textContent=e.target.value.length+' / 2,000');
 const render=()=>{
  widget=window.turnstile.render('#captcha',{sitekey:'0x4AAAAAAFRnWKio_siOWXC_',action:'appeal',theme:'dark',size:'flexible',
   callback:token=>{captcha=token;verification('Verification complete. Ready to send.');},
   'expired-callback':()=>{captcha='';verification('Verification expired. Please verify again.',true);},
   'error-callback':()=>{captcha='';verification('Verification could not load. Check privacy extensions or your connection, then retry.',true);},
   'timeout-callback':()=>{captcha='';verification('Verification timed out. Please retry.',true);}
  });
 };
 const load=()=>{const previous=document.getElementById('turnstile-loader');if(previous)previous.remove();const script=document.createElement('script');script.id='turnstile-loader';script.src='https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';script.async=true;script.onload=render;script.onerror=()=>verification('Verification is blocked or unavailable. Allow challenges.cloudflare.com and retry.',true);document.head.append(script);};
 retry.addEventListener('click',()=>{captcha='';verification('Loading verification…');if(window.turnstile&&widget!==undefined)window.turnstile.reset(widget);else load();});
 setTimeout(()=>{if(!captcha)verification('Complete the verification above. If it is missing, retry or check your browser extensions.',true);},15000);
 form.addEventListener('submit',async event=>{
  event.preventDefault();if(busy)return;
  const username=form.elements.username.value.trim(), explanation=form.elements.explanation.value.trim();
  if(!/^[A-Za-z0-9_]{3,16}$/.test(username)){message('Enter a Minecraft username using 3–16 letters, numbers or underscores.');form.elements.username.focus();return;}
  if(explanation.length<10||explanation.length>2000){message('Tell us what happened using 10–2,000 characters.');form.elements.explanation.focus();return;}
  if(!captcha){message('Complete the verification before sending. If it has not appeared, select Retry verification.');retry.hidden=false;return;}
  busy=true;button.disabled=true;button.textContent='Sending appeal…';form.setAttribute('aria-busy','true');message('Securely sending your appeal…','loading');
  try {
   const response=await fetch('https://plutonium-moderation.vercel.app/api/v1/appeals',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username,explanation,captcha}),signal:AbortSignal.timeout(20000)});
   let body;try{body=await response.json();}catch{throw new Error('The service returned an unexpected response. Your explanation is saved on this page; please retry.');}
   if(!response.ok)throw new Error((body.error||'The appeal could not be sent.')+(body.requestId?' Reference: '+body.requestId:''));
   message(body.message,'success');button.textContent='Send another appeal →';
  }catch(error){message(error.name==='TimeoutError'?'The request timed out. Your explanation is still here; please retry.':error.message||'Unable to connect. Please retry.');button.textContent='Try sending again →';}
  finally{busy=false;button.disabled=false;form.removeAttribute('aria-busy');captcha='';if(window.turnstile&&widget!==undefined)window.turnstile.reset(widget);}
 });
 load();
})();
