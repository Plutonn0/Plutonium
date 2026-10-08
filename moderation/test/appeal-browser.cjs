const {chromium}=require('playwright');
const fs=require('node:fs');const assert=require('node:assert/strict');
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try {
  const p=await browser.newPage({viewport:{width:1280,height:1000}});const errors=[];p.on('pageerror',e=>errors.push(e.message));
  await p.route('https://plutoniumclient.vercel.app/appeal',r=>r.fulfill({contentType:'text/html',body:fs.readFileSync('public/appeal.html','utf8')}));
  await p.route('https://plutonium-moderation.vercel.app/appeal.js',r=>r.fulfill({contentType:'application/javascript',body:fs.readFileSync('public/appeal.js','utf8')}));
  // Only this isolated browser fixture simulates CAPTCHA and API responses. No production requests.
  await p.route('https://challenges.cloudflare.com/**',r=>r.fulfill({contentType:'application/javascript',body:'window.turnstile={render:(el,opts)=>{window.testVerification=opts;return 1},reset:()=>{}}'}));
  let status=503,requests=0;
  await p.route('https://plutonium-moderation.vercel.app/api/v1/appeals',r=>{requests++;return r.fulfill({status,contentType:'application/json',headers:{'access-control-allow-origin':'https://plutoniumclient.vercel.app'},body:JSON.stringify(status===200?{message:'Your test appeal was received.'}:{error:'Service unavailable. Try again.',requestId:'test-reference'})});});
  await p.goto('https://plutoniumclient.vercel.app/appeal');await p.locator('#submit').click();assert.match(await p.locator('#result').innerText(),/Minecraft username/);
  await p.locator('#username').fill('JustQuirk');await p.locator('#submit').click();assert.match(await p.locator('#result').innerText(),/10–2,000/);
  await p.locator('#explanation').fill('Please review this restriction.');await p.locator('#submit').click();assert.match(await p.locator('#result').innerText(),/Complete the verification/);assert.equal(requests,0);
  await p.waitForFunction(()=>window.testVerification);await p.evaluate(()=>window.testVerification.callback('fixture-token'));await p.locator('#submit').click();await p.waitForFunction(()=>document.getElementById('result').textContent.includes('test-reference'));
  assert.equal(await p.locator('#explanation').inputValue(),'Please review this restriction.');assert.equal(await p.locator('#submit').isEnabled(),true);
  status=200;await p.evaluate(()=>window.testVerification.callback('fixture-token'));await p.locator('#submit').click();await p.waitForFunction(()=>document.getElementById('result').dataset.state==='success');assert.equal(requests,2);assert.deepEqual(errors,[]);
  await p.screenshot({path:'../dist/ui-preview/appeal-desktop.png',fullPage:true});
  await p.setViewportSize({width:390,height:844});assert.equal(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await p.screenshot({path:'../dist/ui-preview/appeal-mobile.png',fullPage:true});
  console.log('Passed browser checks: field validation, missing CAPTCHA, failure/retry, preserved text, success and mobile layout.');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
