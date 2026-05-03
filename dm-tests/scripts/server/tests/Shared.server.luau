function Entry()
	local sharedI = Part.New(Environment)
	assert(sharedI.Shared.Yo == nil)

	sharedI.Shared.Yo = "Hi"

	assert(sharedI.Shared.Yo == "Hi")

	sharedI.Shared.Yo = nil

	assert(sharedI.Shared.Yo == nil)

	sharedI.Shared.Miau = 0
	sharedI.Shared.Miau2 = 1

	-- Non string indexer
	sharedI.Shared[1] = "Hi"
	assert(sharedI.Shared[1] == "Hi")

	-- Test __iter
	for k, v in sharedI.Shared do
		if k == "Miau" then
			assert(v == 0)
		elseif k == "Miau2" then
			assert(v == 1)
		elseif k == 1 then
			assert(v == "Hi")
		end
	end

	-- Clear Check
	sharedI.Shared:Clear()

	assert(sharedI.Shared.Miau == nil)
	assert(sharedI.Shared.Miau2 == nil)

	-- Clear Prefix
	sharedI.Shared.a_hello = "hello"
	sharedI.Shared.a_hello2 = "hi"

	sharedI.Shared:ClearPrefix("a_")

	assert(sharedI.Shared.a_hello == nil)
	assert(sharedI.Shared.a_hello2 == nil)
end
