-- @common
if ARGV[2] ~= 'renew' and ARGV[2] ~= 'complete' and ARGV[2] ~= 'release' then error('INVALID_MODE') end
for i = 1, 3 do expect(KEYS[i], 'string') end
if redis.call('GET', KEYS[1]) ~= ARGV[1] or redis.call('GET', KEYS[2]) ~= ARGV[1] then return 0 end
if ARGV[2] == 'renew' then return redis.call('PEXPIRE', KEYS[1], 30000) end
if ARGV[2] == 'complete' then redis.call('SET', KEYS[3], '1') end
redis.call('DEL', KEYS[1])
return 1
